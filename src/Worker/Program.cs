using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using PaymentsWorker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // MongoDB
        var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI")
            ?? throw new InvalidOperationException("MONGODB_URI não configurada");

        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(mongoUri);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            return new MongoClient(settings);
        });

        services.AddSingleton(sp =>
        {
            var url = new MongoUrl(mongoUri);
            var dbName = url.DatabaseName ?? "fcg-db";
            return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
        });

        // Worker
        services.AddHostedService<PaymentEventsWorker>();
    })
    .Build();

Console.WriteLine("[PaymentsWorker] Iniciando...");
await host.RunAsync();

