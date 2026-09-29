using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Data;
using SkinRag.Api.Services;
using SkinRag.Api.Infrastructure;
using System.Threading.RateLimiting;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Infrastructure.AI;
using SkinRag.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("consultation", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("maintenance", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var ollamaBaseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
var timeoutSeconds = builder.Configuration.GetValue<int>("Ollama:RequestTimeoutSeconds", 300);

builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri(ollamaBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

builder.Services.AddSingleton<KnowledgeIndexService>();
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<IOllamaClient>(sp => sp.GetRequiredService<OllamaClient>());
builder.Services.AddHostedService<KnowledgeIndexWorker>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddSingleton<InputGuard>();
builder.Services.AddScoped<IIntentClassifier, IntentClassifier>();
builder.Services.AddScoped<IQueryBuilder, QueryBuilder>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductRetriever, ProductRetriever>();
builder.Services.AddScoped<RecommendationValidator>();
builder.Services.AddScoped<ConsultationService>();

var app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/chat"));
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (IDbContextFactory<AppDbContext> factory, KnowledgeIndexService index, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var databaseReady = await db.Database.CanConnectAsync(ct);
    var knowledgeReady = index.Snapshot().IsReady;
    return Results.Json(new { databaseReady, knowledgeReady }, statusCode: databaseReady && knowledgeReady ? 200 : 503);
});

app.Run();
