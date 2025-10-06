using Helpers;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Executa como Lambda atrás do API Gateway (REST)
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

// Mongo via ENV (injetado pelo template com ssm-secure)
var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI")
    ?? throw new InvalidOperationException("Env MONGODB_URI não definida.");

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoUri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    var url = new MongoUrl(mongoUri);
    var dbName = url.DatabaseName ?? "fase3";
    return sp.GetRequiredService<IMongoClient>().GetDatabase(dbName);
});

// Swagger (só para ajudar no teste)
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

// GET /payments/{purchaseId} => { purchaseId, status }
app.MapGet("/payments/{purchaseId}", async (string purchaseId, IMongoDatabase db) =>
{
    var coll = db.GetCollection<BsonDocument>("Purchases");
    var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(purchaseId)))
                        .FirstOrDefaultAsync();
    if (doc is null) return Results.NotFound(new { error = "not found" });

    var status = doc.GetValue("status", "PENDING").AsString;
    return Results.Ok(new { purchaseId, status });
});

app.Run();
