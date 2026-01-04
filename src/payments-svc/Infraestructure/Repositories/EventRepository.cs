using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class EventRepository(IMongoDatabase db) : IEventRepository
    {
        private readonly IMongoCollection<DomainEvent> _events = db.GetCollection<DomainEvent>("Events");

        public Task AppendEventAsync(DomainEvent ev, CancellationToken ct = default) =>
            _events.InsertOneAsync(ev, cancellationToken: ct);

        public async Task<bool> ExistsBySqsMessageIdAsync(string sqsMessageId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sqsMessageId))
                return false;

            var filter = Builders<DomainEvent>.Filter.Eq(x => x.SqsMessageId, sqsMessageId);
            var exists = await _events.Find(filter).Limit(1).AnyAsync(ct);
            return exists;
        }
    }
}

