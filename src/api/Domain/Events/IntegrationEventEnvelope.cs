namespace Domain.Events
{
    /// <summary>
    /// Envelope padrão para eventos de integração entre microsserviços (SQS).
    /// </summary>
    public record IntegrationEventEnvelope(
        Guid EventId,
        string Type,
        DateTime OccurredAt,
        string Source,
        string AggregateId,
        string? CorrelationId,
        string? CausationId,
        int Version,
        object Data
    )
    {
        public static IntegrationEventEnvelope Create(
            string type,
            string source,
            string aggregateId,
            object data,
            string? correlationId = null,
            string? causationId = null,
            int version = 1)
            => new(
                EventId: Guid.NewGuid(),
                Type: type,
                OccurredAt: DateTime.UtcNow,
                Source: source,
                AggregateId: aggregateId,
                CorrelationId: correlationId,
                CausationId: causationId,
                Version: version,
                Data: data
            );
    }
}


