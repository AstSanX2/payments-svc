using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Application.Services;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Driver;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Worker;

public class PaymentWorker
{
    private readonly IPaymentProcessingService _processor;

    public PaymentWorker()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.Development.json", optional: true)
            .Build();

        var mongoUri = config["MongoDB:ConnectionString"];
        if (string.IsNullOrWhiteSpace(mongoUri))
            throw new InvalidOperationException("MongoDB:ConnectionString não configurado no appsettings.");

        var url = new MongoUrl(mongoUri);
        var client = new MongoClient(mongoUri);
        var db = client.GetDatabase(url.DatabaseName ?? "fcg-db");

        IPurchaseRepository purchaseRepo = new Infraestructure.Repositories.PurchaseRepository(db);
        IEventRepository eventRepo = new Infraestructure.Repositories.EventRepository(db);
        _processor = new PaymentProcessingService(purchaseRepo, eventRepo);
    }

    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        foreach (var record in evnt.Records)
        {
            try
            {
                await _processor.ProcessAsync(record.MessageId, record.Body);
                context.Logger.LogInformation($"Pagamento processado: {record.MessageId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Erro ao processar mensagem {record.MessageId}: {ex.Message}");
            }
        }
    }
}
