using Microsoft.Extensions.Caching.Memory;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Models;

namespace SkinRag.Api.Application.Consultation;

public sealed record ConversationState(Guid Id, string[] UserQuestions, int[] ProductIds, DateTime UpdatedAtUtc)
{
    public string? SearchQuery { get; init; }
    public CatalogFilters? SearchFilters { get; init; }
}

// Product references come only from validated server results, never client assistant history.
public sealed class ConversationStore : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });

    public ConversationState Read(Guid? id) => id.HasValue && _cache.TryGetValue(id.Value, out ConversationState? state) && state is not null ? state : new(Guid.NewGuid(), [], [], DateTime.UtcNow);

    public void Save(ConversationState previous, string question, IEnumerable<int> ids, SearchPlan? plan = null)
    {
        var next = previous with
        {
            UserQuestions = previous.UserQuestions.Append(question).TakeLast(4).ToArray(),
            ProductIds = ids.Distinct().Take(10).ToArray(),
            UpdatedAtUtc = DateTime.UtcNow,
            SearchQuery = plan?.Intent is ConsultationIntent.ProductSearch or ConsultationIntent.SkinConsultation or ConsultationIntent.RoutineRecommendation or ConsultationIntent.FollowUp ? plan.Query : previous.SearchQuery,
            SearchFilters = plan?.Intent is ConsultationIntent.ProductSearch or ConsultationIntent.SkinConsultation or ConsultationIntent.RoutineRecommendation or ConsultationIntent.FollowUp ? QueryBuilder.CopyFilters(plan.Filters) : previous.SearchFilters
        };
        _cache.Set(
            next.Id,
            next,
            new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            });
    }

    public static bool IsRepeated(ConversationState state, string question) => DateTime.UtcNow - state.UpdatedAtUtc < TimeSpan.FromSeconds(30) && state.UserQuestions.Length >= 3
        && state.UserQuestions.TakeLast(3).All(x => x == question);
    public void Dispose() => _cache.Dispose();
}
