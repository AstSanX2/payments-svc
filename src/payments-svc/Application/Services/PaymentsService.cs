using Application.DTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;

namespace Application.Services
{
    public class PaymentsService(
        IPurchaseRepository purchaseRepository,
        IEventRepository eventRepository) : IPaymentsService
    {
        public async Task<PaymentStatusDTO?> GetPaymentStatusAsync(ObjectId purchaseId, CancellationToken ct = default)
        {
            var purchase = await purchaseRepository.GetByIdAsync(purchaseId, ct);

            if (purchase is null)
            {
                // Registra evento de compra não encontrada
                var evNotFound = DomainEvent.Create(
                    aggregateId: purchaseId,
                    type: "PaymentStatusNotFound",
                    data: new Dictionary<string, object?>
                    {
                        ["PurchaseId"] = purchaseId.ToString()
                    }
                );
                await eventRepository.AppendEventAsync(evNotFound, ct);

                return null;
            }

            // Registra evento de consulta de status
            var ev = DomainEvent.Create(
                aggregateId: purchaseId,
                type: "PaymentStatusQueried",
                data: new Dictionary<string, object?>
                {
                    ["PurchaseId"] = purchaseId.ToString(),
                    ["Status"] = purchase.Status
                }
            );
            await eventRepository.AppendEventAsync(ev, ct);

            return new PaymentStatusDTO
            {
                PurchaseId = purchaseId.ToString(),
                Status = purchase.Status,
                Amount = purchase.Amount,
                CreatedAt = purchase.CreatedAt,
                UpdatedAt = purchase.UpdatedAt
            };
        }
    }
}

