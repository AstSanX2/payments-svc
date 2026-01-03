using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace PaymentsWorker;

// Mensagem publicada pelo games-svc (compras)
public record PurchaseMsg(string PurchaseId, string UserId, decimal Amount);

public class PaymentEventsWorker : BackgroundService
{
    private readonly IMongoDatabase _db;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly int _pollIntervalMs;
    private readonly int _maxMessages;

    public PaymentEventsWorker(IMongoDatabase db, IConfiguration configuration)
    {
        _db = db;
        _sqs = CreateSqsClient(configuration);
        _queueUrl = configuration["Sqs:PaymentsQueueUrl"]
            ?? configuration["PAYMENTS_QUEUE_URL"]
            ?? Environment.GetEnvironmentVariable("PAYMENTS_QUEUE_URL")
            ?? throw new InvalidOperationException("Payments queue URL not found (Sqs:PaymentsQueueUrl no appsettings ou env PAYMENTS_QUEUE_URL).");

        _pollIntervalMs = int.TryParse(configuration["Worker:PollIntervalMs"] ?? configuration["POLL_INTERVAL_MS"], out var interval)
            ? interval : 5000;
        _maxMessages = int.TryParse(configuration["Worker:MaxMessages"] ?? configuration["MAX_MESSAGES"], out var max)
            ? max : 10;
    }

    private static IAmazonSQS CreateSqsClient(IConfiguration configuration)
    {
        var serviceUrl = configuration["Sqs:ServiceUrl"] ?? Environment.GetEnvironmentVariable("SQS_SERVICE_URL");
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            // LocalStack ou outro emulador
            var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
            var accessKey = configuration["AWS:AccessKey"];
            var secretKey = configuration["AWS:SecretKey"];
            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
                return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);

            return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);
        }
        // AWS real (credenciais via appsettings ou cadeia default)
        var region = configuration["AWS:Region"] ?? Environment.GetEnvironmentVariable("AWS_REGION");
        var sqsConfig = new AmazonSQSConfig();
        if (!string.IsNullOrWhiteSpace(region))
            sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

        var ak = configuration["AWS:AccessKey"];
        var sk = configuration["AWS:SecretKey"];
        if (!string.IsNullOrWhiteSpace(ak) && !string.IsNullOrWhiteSpace(sk))
            return new AmazonSQSClient(new BasicAWSCredentials(ak, sk), sqsConfig);

        return new AmazonSQSClient(sqsConfig);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"[PaymentsWorker] Escutando fila: {_queueUrl}");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _maxMessages,
                    WaitTimeSeconds = 20, // Long polling
                    VisibilityTimeout = 60
                }, stoppingToken);

                if (response.Messages.Count == 0)
                {
                    await Task.Delay(_pollIntervalMs, stoppingToken);
                    continue;
                }

                foreach (var message in response.Messages)
                {
                    try
                    {
                        await ProcessMessageAsync(message, stoppingToken);
                        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        Console.WriteLine($"[PaymentsWorker] Mensagem processada: {message.MessageId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PaymentsWorker] Erro ao processar mensagem {message.MessageId}: {ex.Message}");
                        // Mensagem volta para a fila após visibility timeout
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PaymentsWorker] Erro no loop: {ex.Message}");
                await Task.Delay(5000, stoppingToken);
            }
        }

        Console.WriteLine("[PaymentsWorker] Worker encerrado");
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        PurchaseMsg? msg;
        try
        {
            msg = JsonSerializer.Deserialize<PurchaseMsg>(message.Body);
        }
        catch
        {
            Console.WriteLine($"[PaymentsWorker] Mensagem inválida: {message.Body}");
            return;
        }

        if (msg is null || !ObjectId.TryParse(msg.PurchaseId, out var purchaseId))
        {
            Console.WriteLine("[PaymentsWorker] Payload sem purchaseId.");
            return;
        }

        if (!ObjectId.TryParse(msg.UserId, out var userId))
        {
            Console.WriteLine("[PaymentsWorker] Payload sem userId.");
            return;
        }

        Console.WriteLine($"[PaymentsWorker] Processando pagamento: {msg.PurchaseId}, valor: {msg.Amount}");

        var purchases = _db.GetCollection<BsonDocument>("Purchases");
        var events = _db.GetCollection<BsonDocument>("Events");

        // Idempotência: verifica se já processou este MessageId
        var existingEvent = await events.Find(
            Builders<BsonDocument>.Filter.Eq("SqsMessageId", message.MessageId)
        ).FirstOrDefaultAsync(ct);

        if (existingEvent != null)
        {
            Console.WriteLine($"[PaymentsWorker] Mensagem {message.MessageId} já processada, ignorando");
            return;
        }

        // Simulação: pagamento aprovado
        var newStatus = "PAID";

        // Atualiza status da compra
        var filter = Builders<BsonDocument>.Filter.Eq("_id", purchaseId);
        var update = Builders<BsonDocument>.Update
            .Set("Status", newStatus)
            .Set("UpdatedAt", DateTime.UtcNow);
        await purchases.UpdateOneAsync(filter, update, cancellationToken: ct);

        // Grava evento (event sourcing) com MessageId para idempotência
        var ev = new BsonDocument
        {
            { "SqsMessageId", message.MessageId },
            { "AggregateId", msg.PurchaseId },
            { "Type", "PaymentProcessed" },
            { "Timestamp", DateTime.UtcNow },
            { "Seq", 1 },
            { "Data", new BsonDocument
                {
                    { "UserId", userId },
                    { "Amount", msg.Amount },
                    { "Status", newStatus }
                }
            }
        };
        await events.InsertOneAsync(ev, cancellationToken: ct);

        Console.WriteLine($"[PaymentsWorker] Pagamento processado: {msg.PurchaseId} -> {newStatus}");
    }
}

