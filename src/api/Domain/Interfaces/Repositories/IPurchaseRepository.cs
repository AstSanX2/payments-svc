using Domain.Entities;
using MongoDB.Bson;

namespace Domain.Interfaces.Repositories
{
    public interface IPurchaseRepository
    {
        Task<Purchase?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
        Task<string?> GetStatusAsync(ObjectId purchaseId, CancellationToken ct = default);

        Task UpdateStatusAsync(ObjectId purchaseId, string newStatus, DateTime updatedAtUtc, CancellationToken ct = default);
    }
}

