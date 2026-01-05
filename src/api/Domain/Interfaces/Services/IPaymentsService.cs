using Application.DTO;
using MongoDB.Bson;

namespace Domain.Interfaces.Services
{
    public interface IPaymentsService
    {
        Task<PaymentStatusDTO?> GetPaymentStatusAsync(ObjectId purchaseId, CancellationToken ct = default);
    }
}

