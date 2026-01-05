using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class OutboxRepository : IOutboxRepository
    {
        private readonly IMongoCollection<OutboxMessage> _outbox;

        public OutboxRepository(IMongoDatabase db)
        {
            _outbox = db.GetCollection<OutboxMessage>(nameof(OutboxMessage));
        }

        public Task EnqueueAsync(OutboxMessage message, CancellationToken ct = default) =>
            _outbox.InsertOneAsync(message, cancellationToken: ct);

        public async Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(int limit, DateTime nowUtc, CancellationToken ct = default)
        {
            var filter = Builders<OutboxMessage>.Filter.And(
                Builders<OutboxMessage>.Filter.Eq(x => x.PublishedAt, null),
                Builders<OutboxMessage>.Filter.Or(
                    Builders<OutboxMessage>.Filter.Eq(x => x.NextAttemptAt, null),
                    Builders<OutboxMessage>.Filter.Lte(x => x.NextAttemptAt, nowUtc)
                )
            );

            return await _outbox.Find(filter)
                .SortBy(x => x.CreatedAt)
                .Limit(Math.Clamp(limit, 1, 50))
                .ToListAsync(ct);
        }

        public Task MarkPublishedAsync(OutboxMessage message, string? sqsMessageId, DateTime publishedAtUtc, CancellationToken ct = default)
        {
            var filter = Builders<OutboxMessage>.Filter.Eq(x => x._id, message._id);
            var update = Builders<OutboxMessage>.Update
                .Set(x => x.PublishedAt, publishedAtUtc)
                .Set(x => x.LastSqsMessageId, sqsMessageId)
                .Unset(x => x.NextAttemptAt)
                .Unset(x => x.LastError);
            return _outbox.UpdateOneAsync(filter, update, cancellationToken: ct);
        }

        public Task MarkFailedAsync(OutboxMessage message, string error, DateTime nextAttemptAtUtc, CancellationToken ct = default)
        {
            var filter = Builders<OutboxMessage>.Filter.Eq(x => x._id, message._id);
            var update = Builders<OutboxMessage>.Update
                .Inc(x => x.Attempts, 1)
                .Set(x => x.LastError, error)
                .Set(x => x.NextAttemptAt, nextAttemptAtUtc);
            return _outbox.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
    }
}


