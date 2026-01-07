using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Entities
{
    public class OutboxMessage
    {
        [BsonId]
        public ObjectId _id { get; set; } = ObjectId.GenerateNewId();

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid EventId { get; set; }
        public string EventType { get; set; } = default!;
        public string Source { get; set; } = default!;
        public string AggregateId { get; set; } = default!;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public int Version { get; set; } = 1;

        public string Destination { get; set; } = default!;
        public string Body { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }

        public int Attempts { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public string? LastError { get; set; }
        public string? LastSqsMessageId { get; set; }
    }
}


