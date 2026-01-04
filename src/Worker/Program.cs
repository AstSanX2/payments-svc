using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using PaymentsWorker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, cfg) =>
    {
        // Usar APENAS appsettings (sem env vars)
        cfg.Sources.Clear();
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        cfg.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        // MongoDB
        var mongoUri =
            context.Configuration["MongoDB:ConnectionString"];

        if (string.IsNullOrWhiteSpace(mongoUri))
            throw new InvalidOperationException("MongoDB connection string not found (MongoDB:ConnectionString no appsettings).");

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

        // Reusa services/repos do payments-svc (clean architecture)
        services.AddScoped<Domain.Interfaces.Repositories.IPurchaseRepository, Infraestructure.Repositories.PurchaseRepository>();
        services.AddScoped<Domain.Interfaces.Repositories.IEventRepository, Infraestructure.Repositories.EventRepository>();
        services.AddScoped<Domain.Interfaces.Services.IPaymentProcessingService, Application.Services.PaymentProcessingService>();

        // Worker
        services.AddHostedService<PaymentEventsWorker>();
    })
    .Build();

Console.WriteLine("[PaymentsWorker] Iniciando...");
await host.RunAsync();

