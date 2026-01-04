using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class PurchaseRepository(IMongoDatabase db) : IPurchaseRepository
    {
        private readonly IMongoCollection<Purchase> _purchases = db.GetCollection<Purchase>("Purchases");

        public async Task<Purchase?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        {
            return await _purchases
                .Find(Builders<Purchase>.Filter.Eq(p => p._id, id))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<string?> GetStatusAsync(ObjectId purchaseId, CancellationToken ct = default)
        {
            var purchase = await GetByIdAsync(purchaseId, ct);
            return purchase?.Status;
        }

        public async Task UpdateStatusAsync(ObjectId purchaseId, string newStatus, DateTime updatedAtUtc, CancellationToken ct = default)
        {
            var filter = Builders<Purchase>.Filter.Eq(p => p._id, purchaseId);
            var update = Builders<Purchase>.Update
                .Set(p => p.Status, newStatus)
                .Set(p => p.UpdatedAt, updatedAtUtc);

            await _purchases.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
    }
}

