using Application.DTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using System.Text.Json;

namespace Application.Services
{
    /// <summary>
    /// Processa mensagens de compra vindas da SQS (worker), com idempotência.
    /// </summary>
    public class PaymentProcessingService(
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepository) : IPaymentProcessingService
    {
        public async Task ProcessAsync(string sqsMessageId, string messageBody, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(messageBody))
                return;

            // Idempotência: se já processou este MessageId, ignora
            if (!string.IsNullOrWhiteSpace(sqsMessageId))
            {
                var alreadyProcessed = await eventRepository.ExistsBySqsMessageIdAsync(sqsMessageId, ct);
                if (alreadyProcessed) return;
            }

            PurchaseMessageDTO? msg;
            try
            {
                msg = JsonSerializer.Deserialize<PurchaseMessageDTO>(messageBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return;
            }

            if (msg is null) return;
            if (!ObjectId.TryParse(msg.PurchaseId, out var purchaseId)) return;
            if (!ObjectId.TryParse(msg.UserId, out var userId)) return;

            // Simulação: pagamento aprovado
            var newStatus = "PAID";
            var now = DateTime.UtcNow;

            await purchaseRepository.UpdateStatusAsync(purchaseId, newStatus, now, ct);

            // Grava evento de processamento (com MessageId para idempotência)
            var ev = DomainEvent.Create(
                aggregateId: purchaseId,
                type: "PaymentProcessed",
                data: new Dictionary<string, object?>
                {
                    ["UserId"] = userId,
                    ["Amount"] = msg.Amount,
                    ["Status"] = newStatus
                },
                seq: 1
            );
            ev.SqsMessageId = string.IsNullOrWhiteSpace(sqsMessageId) ? null : sqsMessageId;

            await eventRepository.AppendEventAsync(ev, ct);
        }
    }
}


