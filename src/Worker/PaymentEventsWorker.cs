using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text.Json;

namespace PaymentsWorker;

public class PaymentEventsWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("payments-worker");
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;
    private readonly int _pollIntervalMs;
    private readonly int _maxMessages;
    private readonly IPaymentProcessingService _processor;

    public PaymentEventsWorker(IPaymentProcessingService processor, IConfiguration configuration)
    {
        _processor = processor;
        _sqs = CreateSqsClient(configuration);
        _queueUrl = configuration["Sqs:PaymentsQueueUrl"]
            ?? throw new InvalidOperationException("Payments queue URL not found (Sqs:PaymentsQueueUrl no appsettings).");

        _pollIntervalMs = int.TryParse(configuration["Worker:PollIntervalMs"], out var interval)
            ? interval : 5000;
        _maxMessages = int.TryParse(configuration["Worker:MaxMessages"], out var max)
            ? max : 10;
    }

    private static IAmazonSQS CreateSqsClient(IConfiguration configuration)
    {
        var serviceUrl = configuration["Sqs:ServiceUrl"];
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            // LocalStack ou outro emulador
            var config = new AmazonSQSConfig { ServiceURL = serviceUrl };
            var accessKey = configuration["AWS:AccessKey"];
            var secretKey = configuration["AWS:SecretKey"];
            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
                return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);

            // Se não tiver keys no appsettings, usa a cadeia default
            return new AmazonSQSClient(config);
        }
        // AWS real (credenciais via appsettings ou cadeia default)
        var region = configuration["AWS:Region"];
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
                using var pollActivity = ActivitySource.StartActivity("sqs receive", ActivityKind.Consumer);
                pollActivity?.SetTag("messaging.system", "aws.sqs");
                pollActivity?.SetTag("messaging.destination", "payments-queue");
                pollActivity?.SetTag("messaging.operation", "receive");

                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _maxMessages,
                    WaitTimeSeconds = 20, // Long polling
                    VisibilityTimeout = 60
                }, stoppingToken);

                var messages = response.Messages ?? new List<Message>();
                if (messages.Count == 0)
                {
                    await Task.Delay(_pollIntervalMs, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    try
                    {
                        using var activity = StartConsumerActivity(message);
                        await _processor.ProcessAsync(message.MessageId, message.Body, stoppingToken);
                        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        Console.WriteLine($"[PaymentsWorker] Mensagem processada: {message.MessageId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PaymentsWorker] Erro ao processar mensagem {message.MessageId}: {ex}");
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
                Console.WriteLine($"[PaymentsWorker] Erro no loop: {ex}");
                await Task.Delay(5000, stoppingToken);
            }
        }

        Console.WriteLine("[PaymentsWorker] Worker encerrado");
    }

    private static Activity? StartConsumerActivity(Message message)
    {
        string? eventType = null;
        string? correlationId = null;

        try
        {
            using var doc = JsonDocument.Parse(message.Body);
            var root = doc.RootElement;

            // Tenta ler do envelope padrão (Type/CorrelationId), aceitando variações de casing
            if (TryGetString(root, "Type", out var t) || TryGetString(root, "type", out t))
                eventType = t;
            if (TryGetString(root, "CorrelationId", out var c) || TryGetString(root, "correlationId", out c))
                correlationId = c;
        }
        catch
        {
            // best-effort
        }

        Activity? activity;
        var name = string.IsNullOrWhiteSpace(eventType) ? "sqs message consume" : $"{eventType} consume";

        if (!string.IsNullOrWhiteSpace(correlationId)
            && ActivityContext.TryParse(correlationId, null, out var parentContext))
        {
            activity = ActivitySource.StartActivity(name, ActivityKind.Consumer, parentContext);
        }
        else
        {
            activity = ActivitySource.StartActivity(name, ActivityKind.Consumer);
        }

        activity?.SetTag("messaging.system", "aws.sqs");
        activity?.SetTag("messaging.destination", "payments-queue");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message_id", message.MessageId);
        if (!string.IsNullOrWhiteSpace(eventType)) activity?.SetTag("fcg.event_type", eventType);

        return activity;
    }

    private static bool TryGetString(JsonElement obj, string propertyName, out string? value)
    {
        value = null;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty(propertyName, out var prop)) return false;
        if (prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString();
        return true;
    }
}

