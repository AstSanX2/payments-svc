using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly IMongoCollection<DomainEvent> _events;

        public EventRepository(IMongoDatabase db)
        {
            _events = db.GetCollection<DomainEvent>("Events");
            EnsureIdempotencyIndex();
        }

        private void EnsureIdempotencyIndex()
        {
            // Único por SqsMessageId (apenas quando existe e não é null/empty)
            try
            {
                var keys = Builders<DomainEvent>.IndexKeys.Ascending(x => x.SqsMessageId);
                var partial = Builders<DomainEvent>.Filter.And(
                    Builders<DomainEvent>.Filter.Exists(nameof(DomainEvent.SqsMessageId), true),
                    Builders<DomainEvent>.Filter.Ne(x => x.SqsMessageId, null),
                    Builders<DomainEvent>.Filter.Ne(x => x.SqsMessageId, string.Empty)
                );

                _events.Indexes.CreateOne(new CreateIndexModel<DomainEvent>(
                    keys,
                    new CreateIndexOptions<DomainEvent>
                    {
                        Name = "ux_sqsMessageId",
                        Unique = true,
                        PartialFilterExpression = partial
                    }));
            }
            catch
            {
                // best-effort
            }
        }

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

