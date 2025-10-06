using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Payments.Worker;

// Mensagem publicada pelo games-svc
public record PurchaseMsg(string PurchaseId, string UserId, decimal Amount);

public class PaymentWorker
{
    private readonly IMongoDatabase _db;

    public PaymentWorker()
    {
        var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI")
            ?? throw new InvalidOperationException("Env MONGODB_URI não definida.");
        var url = new MongoUrl(mongoUri);
        var client = new MongoClient(mongoUri);
        _db = client.GetDatabase(url.DatabaseName ?? "fcg-db");
    }

    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var purchases = _db.GetCollection<BsonDocument>("Purchases");
        var events = _db.GetCollection<BsonDocument>("Events");

        foreach (var record in evnt.Records)
        {
            PurchaseMsg? msg;
            try
            {
                msg = JsonSerializer.Deserialize<PurchaseMsg>(record.Body);
            }
            catch
            {
                context.Logger.LogError($"Mensagem inválida: {record.Body}");
                continue;
            }

            if (msg is null || !ObjectId.TryParse(msg.PurchaseId, out var purchaseId))
            {
                context.Logger.LogError("Payload sem purchaseId.");
                continue;
            }

            if (!ObjectId.TryParse(msg.UserId, out var userId))
            {
                context.Logger.LogError("Payload sem purchaseId.");
                continue;
            }

            // Simulação: pagamento aprovado
            var newStatus = "PAID";

            // Atualiza status da compra
            var f = Builders<BsonDocument>.Filter.Eq("_id", purchaseId);
            var u = Builders<BsonDocument>.Update
                        .Set("Status", newStatus)
                        .Set("UpdatedAt", DateTime.UtcNow);
            await purchases.UpdateOneAsync(f, u);

            // Grava evento (event sourcing)
            var ev = new BsonDocument {
                { "aggregateId", msg.PurchaseId },
                { "type", "PaymentProcessed" },
                { "timestamp", DateTime.UtcNow },
                { "seq", 1 },
                { "data", new BsonDocument {
                    { "userId", userId },
                    { "amount", msg.Amount }
                } }
            };
            await events.InsertOneAsync(ev);

            context.Logger.LogInformation($"Pagamento processado: {msg.PurchaseId}");
        }
    }
}
