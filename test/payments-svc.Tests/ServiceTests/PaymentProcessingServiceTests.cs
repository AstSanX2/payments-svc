using Application.Services;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;
using System.Text.Json;
using Xunit;

namespace payments_svc.Tests.ServiceTests
{
    public class PaymentProcessingServiceTests
    {
        private readonly Mock<IPurchaseRepository> _purchaseRepo;
        private readonly Mock<IEventRepository> _eventRepo;
        private readonly Mock<IOutboxRepository> _outboxRepo;
        private readonly Mock<IConfiguration> _configuration;
        private readonly IPaymentProcessingService _service;

        public PaymentProcessingServiceTests()
        {
            _purchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            _eventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            _outboxRepo = new Mock<IOutboxRepository>(MockBehavior.Strict);
            _configuration = new Mock<IConfiguration>();

            _outboxRepo.Setup(o => o.EnqueueAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Por padrão, não configurar fila de eventos (não deve enfileirar PaymentProcessed)
            _configuration.Setup(c => c["Sqs:PaymentsEventsQueueUrl"]).Returns((string?)null);

            _service = new PaymentProcessingService(_purchaseRepo.Object, _eventRepo.Object, _outboxRepo.Object, _configuration.Object);
        }

        [Fact(DisplayName = "ProcessAsync deve ignorar payload inválido (json quebrado)")]
        public async Task ProcessAsync_InvalidJson_DoesNothing()
        {
            _eventRepo.Setup(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await _service.ProcessAsync("m1", "{not-json}");

            _purchaseRepo.VerifyNoOtherCalls();
            _eventRepo.Verify(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()), Times.Once);
            _eventRepo.VerifyNoOtherCalls();
            _outboxRepo.VerifyNoOtherCalls();
        }

        [Fact(DisplayName = "ProcessAsync deve ser idempotente por SqsMessageId")]
        public async Task ProcessAsync_AlreadyProcessed_Ignores()
        {
            _eventRepo.Setup(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var body = JsonSerializer.Serialize(new { purchaseId = ObjectId.GenerateNewId().ToString(), userId = ObjectId.GenerateNewId().ToString(), amount = 10m });

            await _service.ProcessAsync("m1", body);

            _purchaseRepo.VerifyNoOtherCalls();
            _eventRepo.Verify(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()), Times.Once);
            _eventRepo.VerifyNoOtherCalls();
            _outboxRepo.VerifyNoOtherCalls();
        }

        [Fact(DisplayName = "ProcessAsync deve atualizar status e registrar evento PaymentProcessed")]
        public async Task ProcessAsync_ValidMessage_UpdatesPurchaseAndAppendsEvent()
        {
            var purchaseId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 59.90m;

            _eventRepo.Setup(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            _purchaseRepo.Setup(r => r.UpdateStatusAsync(purchaseId, "PAID", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _eventRepo.Setup(r => r.AppendEventAsync(It.Is<DomainEvent>(ev =>
                    ev.Type == "PaymentProcessed" &&
                    ev.AggregateId == purchaseId &&
                    ev.SqsMessageId == "m1"
                ), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Payload legado (compat) ainda deve funcionar
            var body = JsonSerializer.Serialize(new { purchaseId = purchaseId.ToString(), userId = userId.ToString(), amount });

            await _service.ProcessAsync("m1", body);

            _eventRepo.Verify(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()), Times.Once);
            _purchaseRepo.Verify(r => r.UpdateStatusAsync(purchaseId, "PAID", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
            _eventRepo.Verify(r => r.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            _outboxRepo.VerifyNoOtherCalls();
        }

        [Fact(DisplayName = "ProcessAsync deve ignorar quando purchaseId/userId não são ObjectId válidos")]
        public async Task ProcessAsync_InvalidIds_Ignores()
        {
            _eventRepo.Setup(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var body = JsonSerializer.Serialize(new { purchaseId = "x", userId = "y", amount = 10m });

            await _service.ProcessAsync("m1", body);

            _purchaseRepo.VerifyNoOtherCalls();
            _eventRepo.Verify(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()), Times.Once);
            _eventRepo.VerifyNoOtherCalls();
            _outboxRepo.VerifyNoOtherCalls();
        }

        [Fact(DisplayName = "ProcessAsync deve aceitar envelope padrão IntegrationEventEnvelope")]
        public async Task ProcessAsync_Envelope_Works()
        {
            var purchaseId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 10m;

            _eventRepo.Setup(r => r.ExistsBySqsMessageIdAsync("m1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            _purchaseRepo.Setup(r => r.UpdateStatusAsync(purchaseId, "PAID", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _eventRepo.Setup(r => r.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var envelope = new
            {
                eventId = Guid.NewGuid(),
                type = "PaymentInitiated",
                occurredAt = DateTime.UtcNow,
                source = "games-svc",
                aggregateId = purchaseId.ToString(),
                correlationId = "c1",
                causationId = (string?)null,
                version = 1,
                data = new { PurchaseId = purchaseId.ToString(), UserId = userId.ToString(), Amount = amount }
            };
            var body = JsonSerializer.Serialize(envelope);

            await _service.ProcessAsync("m1", body);

            _purchaseRepo.Verify(r => r.UpdateStatusAsync(purchaseId, "PAID", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
            _eventRepo.Verify(r => r.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}


