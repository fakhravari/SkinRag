using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Data;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Infrastructure.Ollama;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Services.Catalog;
using SkinRag.Api.Services.Knowledge;

namespace SkinRag.Api.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSkinRag(this IServiceCollection services, IConfiguration configuration)
    {
        AddWebServices(services);
        AddDataServices(services, configuration);
        AddModelServices(services, configuration);
        AddConsultationServices(services);
        return services;
    }

    private static void AddWebServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddRateLimitPolicy(options, "consultation", 12);
        });
    }

    private static void AddRateLimitPolicy(RateLimiterOptions options, string name, int permitLimit)
    {
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    private static void AddDataServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<CatalogService>();
    }

    private static void AddModelServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("Ollama",
            client =>
            {
                client.BaseAddress = new Uri(configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
                client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Ollama:RequestTimeoutSeconds", 300));
            });
        services.AddSingleton<OllamaClient>();
        services.AddSingleton<IOllamaClient>(provider => provider.GetRequiredService<OllamaClient>());
        services.AddSingleton<KnowledgeIndexService>();
        services.AddHostedService<KnowledgeIndexWorker>();
    }

    private static void AddConsultationServices(IServiceCollection services)
    {
        services.AddSingleton<ConversationStore>();
        services.AddSingleton<InputGuard>();
        services.AddScoped<IIntentClassifier, IntentClassifier>();
        services.AddScoped<IQueryBuilder, QueryBuilder>();
        services.AddScoped<IProductRetriever, ProductRetriever>();
        services.AddScoped<RecommendationValidator>();
        services.AddScoped<ConsultationService>();
    }
}
