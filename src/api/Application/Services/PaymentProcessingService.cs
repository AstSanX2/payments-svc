using Application.DTO;
using Domain.Entities;
using Domain.Events;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using System.Diagnostics;
using System.Text.Json;

namespace Application.Services
{
    /// <summary>
    /// Processa mensagens de compra vindas da SQS (worker), com idempotência.
    /// </summary>
    public class PaymentProcessingService(
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepository,
        IOutboxRepository outboxRepository,
        IConfiguration configuration) : IPaymentProcessingService
    {
        private const string SourceName = "payments-svc";

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

            // Suporta envelope padrão (IntegrationEventEnvelope) e também payload legado (PurchaseMessageDTO).
            var (ok, purchaseId, userId, amount) = TryParsePaymentInitiated(messageBody);
            if (!ok) return;

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
                    ["Amount"] = amount,
                    ["Status"] = newStatus
                },
                seq: 1
            );
            ev.SqsMessageId = string.IsNullOrWhiteSpace(sqsMessageId) ? null : sqsMessageId;

            await eventRepository.AppendEventAsync(ev, ct);

            // Publica evento de integração (assíncrono) via Outbox
            _ = EnqueuePaymentProcessedAsync(purchaseId, userId, amount);
        }

        private async Task EnqueuePaymentProcessedAsync(ObjectId purchaseId, ObjectId userId, decimal amount)
        {
            try
            {
                var queueUrl = configuration["Sqs:PaymentsEventsQueueUrl"];
                if (string.IsNullOrWhiteSpace(queueUrl)) return;

                var correlationId = Activity.Current?.TraceId.ToString();
                var env = IntegrationEventEnvelope.Create(
                    type: "PaymentProcessed",
                    source: SourceName,
                    aggregateId: purchaseId.ToString(),
                    data: new Dictionary<string, object?>
                    {
                        ["PurchaseId"] = purchaseId.ToString(),
                        ["UserId"] = userId.ToString(),
                        ["Amount"] = amount,
                        ["Status"] = "PAID"
                    },
                    correlationId: correlationId
                );

                var body = JsonSerializer.Serialize(env);
                var outbox = new OutboxMessage
                {
                    EventId = env.EventId,
                    EventType = env.Type,
                    Source = env.Source,
                    AggregateId = env.AggregateId,
                    CorrelationId = env.CorrelationId,
                    CausationId = env.CausationId,
                    Version = env.Version,
                    Destination = queueUrl,
                    Body = body
                };

                await outboxRepository.EnqueueAsync(outbox, CancellationToken.None);
            }
            catch
            {
                // Não afeta o fluxo principal.
            }
        }

        private static (bool ok, ObjectId purchaseId, ObjectId userId, decimal amount) TryParsePaymentInitiated(string messageBody)
        {
            // 1) Envelope padrão
            try
            {
                var env = JsonSerializer.Deserialize<IntegrationEventEnvelope>(messageBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (env is not null && env.Data is JsonElement dataEl)
                {
                    // Aceita tanto PaymentInitiated quanto outros tipos, desde que carregue PurchaseId/UserId/Amount.
                    if (dataEl.TryGetProperty("PurchaseId", out var pid) &&
                        dataEl.TryGetProperty("UserId", out var uid) &&
                        dataEl.TryGetProperty("Amount", out var amt))
                    {
                        var purchaseIdStr = pid.GetString();
                        var userIdStr = uid.GetString();
                        var amount = amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : 0m;

                        if (ObjectId.TryParse(purchaseIdStr, out var purchaseId) &&
                            ObjectId.TryParse(userIdStr, out var userId))
                        {
                            return (true, purchaseId, userId, amount);
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 2) Payload legado (compat)
            try
            {
                var msg = JsonSerializer.Deserialize<PurchaseMessageDTO>(messageBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (msg is null) return default;
                if (!ObjectId.TryParse(msg.PurchaseId, out var purchaseId)) return default;
                if (!ObjectId.TryParse(msg.UserId, out var userId)) return default;
                return (true, purchaseId, userId, msg.Amount);
            }
            catch
            {
                return default;
            }
        }
    }
}


