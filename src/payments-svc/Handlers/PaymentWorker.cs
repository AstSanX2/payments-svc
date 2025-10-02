using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Payments.Worker;

// Mensagem publicada pelo games-svc
public record PurchaseMsg(string purchaseId, string userId, decimal amount);

public class PaymentWorker
{
    private readonly IMongoDatabase _db;

    public PaymentWorker()
    {
        var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI")
            ?? throw new InvalidOperationException("Env MONGODB_URI não definida.");
        var url = new MongoUrl(mongoUri);
        var client = new MongoClient(mongoUri);
        _db = client.GetDatabase(url.DatabaseName ?? "fase3");
    }

    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var purchases = _db.GetCollection<BsonDocument>("purchases");
        var events = _db.GetCollection<BsonDocument>("events");

        foreach (var record in evnt.Records)
        {
            PurchaseMsg? msg = null;
            try
            {
                msg = JsonSerializer.Deserialize<PurchaseMsg>(record.Body);
            }
            catch
            {
                context.Logger.LogError($"Mensagem inválida: {record.Body}");
                continue;
            }

            if (msg is null || string.IsNullOrWhiteSpace(msg.purchaseId))
            {
                context.Logger.LogError("Payload sem purchaseId.");
                continue;
            }

            // Simulação: pagamento aprovado
            var newStatus = "PAID";

            // Atualiza status da compra
            var f = Builders<BsonDocument>.Filter.Eq("_id", msg.purchaseId);
            var u = Builders<BsonDocument>.Update
                        .Set("status", newStatus)
                        .Set("updatedAt", DateTime.UtcNow);
            await purchases.UpdateOneAsync(f, u);

            // Grava evento (event sourcing)
            var ev = new BsonDocument {
                { "aggregateId", msg.purchaseId },
                { "type", "PaymentProcessed" },
                { "timestamp", DateTime.UtcNow },
                { "seq", 1 },
                { "data", new BsonDocument {
                    { "userId", msg.userId },
                    { "amount", msg.amount }
                } }
            };
            await events.InsertOneAsync(ev);

            context.Logger.LogInformation($"Pagamento processado: {msg.purchaseId}");
        }
    }
}
