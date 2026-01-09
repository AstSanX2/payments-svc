using Application.Services;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Helpers;
using Infraestructure.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text.Json;
using Middlewares;

var builder = WebApplication.CreateBuilder(args);

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

// ------------------------------------------------------
// Configuração: usar APENAS appsettings (sem env vars)
// ------------------------------------------------------
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// ------------------------------------------------------
// Kestrel otimizado para rodar em container/Kubernetes
// ------------------------------------------------------
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Para funcionar bem atrás de ingress/nginx/ALB
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var env = builder.Environment;
var config = builder.Configuration;

var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName: "payments-api"))
        .WithTracing(t =>
        {
            t.SetSampler(new AlwaysOnSampler());
            t.AddSource("payments-api.outbox");
            t.AddAspNetCoreInstrumentation();
            t.AddHttpClientInstrumentation();
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });
}

static string FirstNonEmpty(params string?[] vals) =>
    vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

// ----------------- MongoDB -----------------
var mongoUri = FirstNonEmpty(
    config["MongoDB:ConnectionString"]
);

if (string.IsNullOrWhiteSpace(mongoUri))
    throw new InvalidOperationException("MongoDB connection string not found (MongoDB:ConnectionString no appsettings).");

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoUri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    var url = new MongoUrl(mongoUri);
    var dbName = url.DatabaseName ?? "fcg-db";
    return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
});

// ----------------- DI (Repositories & Services) -----------------
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IPaymentsService, PaymentsService>();
builder.Services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();

// Outbox publisher (SQS integration events)
builder.Services.AddHostedService<Application.Services.OutboxPublisherHostedService>();

// ----------------- MVC + Swagger -----------------
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PaymentsSvc", Version = "v1" });
});

var app = builder.Build();

if (string.Equals(Environment.GetEnvironmentVariable("FCG_LOG_HTTP_BODIES"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseMiddleware<HttpBodyLoggingMiddleware>();
}

// Para funcionar bem atrás de proxy reverso / ingress
app.UseForwardedHeaders();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// ------------------------------------------------------
// Endpoints para probes do Kubernetes
// ------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    svc = "payments",
    env = env.EnvironmentName
}));

app.MapGet("/ready", () => Results.Ok(new
{
    ready = true,
    svc = "payments"
}));

app.MapGet("/", () => "PaymentsSvc up & running (container mode)");

app.Run();
