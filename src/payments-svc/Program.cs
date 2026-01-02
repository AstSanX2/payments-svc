using Helpers;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Kestrel otimizado para rodar em container/Kubernetes
// ------------------------------------------------------
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

var env = builder.Environment;
var config = builder.Configuration;

static string FirstNonEmpty(params string?[] vals) =>
    vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

// Mongo via ENV (recomendado em containers) ou appsettings (Dev)
var mongoUri = FirstNonEmpty(
    Environment.GetEnvironmentVariable("MONGODB_URI"),
    config["MongoDB:ConnectionString"]
);

if (string.IsNullOrWhiteSpace(mongoUri))
    throw new InvalidOperationException("MongoDB connection string not found (env MONGODB_URI ou MongoDB:ConnectionString).");

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

// Swagger (s� para ajudar no teste)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PaymentsSvc", Version = "v1" });
});

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Health
app.MapGet("/health", () => Results.Ok(new { ok = true, svc = "payments" }));
app.MapGet("/ready", () => Results.Ok(new { ready = true, svc = "payments" }));
app.MapGet("/", () => "PaymentsSvc up & running (container mode)");

// GET /payments/{purchaseId} => { purchaseId, status }
app.MapGet("/payments/{purchaseId}", async (string purchaseId, IMongoDatabase db) =>
{
    var coll = db.GetCollection<BsonDocument>("Purchases");
    var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(purchaseId)))
                        .FirstOrDefaultAsync();
    if (doc is null) return Results.NotFound(new { error = "not found" });

    var status = doc.GetValue("Status", "PENDING").AsString;
    return Results.Ok(new { purchaseId, status });
});

app.Run();
