using MongoDB.Bson;
using System.Collections;

namespace Domain.Entities
{
    public class DomainEvent
    {
        public ObjectId _id { get; set; }
        public ObjectId AggregateId { get; set; }
        // Para eventos processados pelo Worker (idempotência)
        public string? SqsMessageId { get; set; }
        public string Type { get; set; } = default!;
        public DateTime Timestamp { get; set; }
        public long? Seq { get; set; }
        public BsonDocument Data { get; set; } = default!;

        public static DomainEvent Create(
            ObjectId aggregateId,
            string type,
            IDictionary<string, object?> data,
            long? seq = null)
        {
            var doc = new BsonDocument();
            foreach (var kv in data)
            {
                doc[kv.Key] = ToBsonValue(kv.Value);
            }

            return new DomainEvent
            {
                _id = ObjectId.GenerateNewId(),
                AggregateId = aggregateId,
                Type = type,
                Timestamp = DateTime.UtcNow,
                Seq = seq,
                Data = doc
            };
        }

        private static BsonValue ToBsonValue(object? value)
        {
            if (value is null) return BsonNull.Value;
            if (value is BsonValue bson) return bson;

            switch (value)
            {
                case string s: return new BsonString(s);
                case bool b: return new BsonBoolean(b);
                case int i: return new BsonInt32(i);
                case long l: return new BsonInt64(l);
                case double d: return new BsonDouble(d);
                case decimal m: return new BsonDecimal128(m);
                case DateTime dt: return new BsonDateTime(dt);
                case ObjectId oid: return oid;

                case IDictionary<string, object?> map:
                    {
                        var nested = new BsonDocument();
                        foreach (var kv in map)
                            nested[kv.Key] = ToBsonValue(kv.Value);
                        return nested;
                    }

                case IEnumerable enumerable when value is not string:
                    {
                        var arr = new BsonArray();
                        foreach (var item in enumerable)
                            arr.Add(ToBsonValue(item));
                        return arr;
                    }

                default:
                    return BsonDocumentWrapper.Create(value);
            }
        }
    }
}

