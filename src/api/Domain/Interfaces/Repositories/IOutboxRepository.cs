using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IOutboxRepository
    {
        Task EnqueueAsync(OutboxMessage message, CancellationToken ct = default);
        Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(int limit, DateTime nowUtc, CancellationToken ct = default);
        Task MarkPublishedAsync(OutboxMessage message, string? sqsMessageId, DateTime publishedAtUtc, CancellationToken ct = default);
        Task MarkFailedAsync(OutboxMessage message, string error, DateTime nextAttemptAtUtc, CancellationToken ct = default);
    }
}


