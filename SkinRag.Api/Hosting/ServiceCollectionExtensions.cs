using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Infrastructure.Integrations.Ollama;
using SkinRag.Api.Infrastructure.Catalog;
using SkinRag.Api.Infrastructure.Knowledge;
using SkinRag.Api.Infrastructure.Telemetry;
using SkinRag.Api.Presentation.Middleware;

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
    }

    private static void AddDataServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<SkinRagDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<CatalogQueryService>();
        services.AddScoped<ICatalogQueryService>(provider => provider.GetRequiredService<CatalogQueryService>());
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
        services.AddSingleton<IKnowledgeIndex>(provider => provider.GetRequiredService<KnowledgeIndexService>());
        services.AddHostedService<KnowledgeIndexBackgroundService>();
        services.AddSingleton<ConsultationPerformanceQueue>();
        services.AddSingleton<IConsultationPerformanceSink>(provider => provider.GetRequiredService<ConsultationPerformanceQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<ConsultationPerformanceQueue>());
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
