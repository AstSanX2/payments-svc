using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IEventRepository
    {
        Task AppendEventAsync(DomainEvent ev, CancellationToken ct = default);

        /// <summary>
        /// Verifica idempotência do worker: se já existe evento com o SqsMessageId.
        /// </summary>
        Task<bool> ExistsBySqsMessageIdAsync(string sqsMessageId, CancellationToken ct = default);
    }
}

