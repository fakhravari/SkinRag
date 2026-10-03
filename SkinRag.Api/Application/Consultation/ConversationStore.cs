using Microsoft.Extensions.Caching.Memory;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;

namespace SkinRag.Api.Application.Consultation;

public sealed record ConversationState(Guid Id, string[] UserQuestions, int[] ProductIds, DateTime UpdatedAtUtc)
{
    public string? SearchQuery { get; init; }
    public CatalogFilters? SearchFilters { get; init; }
    public string[] RecentUserMessages { get; init; } = [];
    public decimal? PendingBudgetRials { get; init; }
    public decimal? PendingMinimumBudgetRials { get; init; }
}

// Product references come only from validated server results, never client assistant history.
public sealed class ConversationStore : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });

    public void Dispose()
    {
        _cache.Dispose();
    }

    public ConversationState Read(Guid? id)
    {
        return id.HasValue && _cache.TryGetValue(id.Value, out ConversationState? state) && state is not null
            ? state
            : new ConversationState(Guid.NewGuid(), [], [], DateTime.UtcNow);
    }

    public void Save(ConversationState previous, string question, IEnumerable<int> ids, SearchPlan? plan = null)
    {
        var next = previous with
        {
            UserQuestions = previous.UserQuestions.Append(question).TakeLast(4).ToArray(),
            RecentUserMessages = previous.RecentUserMessages.Append(question).TakeLast(4).ToArray(),
            PendingBudgetRials = null,
            PendingMinimumBudgetRials = null,
            ProductIds = ids.Distinct().Take(10).ToArray(),
            UpdatedAtUtc = DateTime.UtcNow,
            SearchQuery =
            plan?.Intent is ConsultationIntent.ProductSearch or ConsultationIntent.SkinConsultation
                or ConsultationIntent.RoutineRecommendation or ConsultationIntent.FollowUp
                ? plan.Query
                : previous.SearchQuery,
            SearchFilters =
            plan?.Intent is ConsultationIntent.ProductSearch or ConsultationIntent.SkinConsultation
                or ConsultationIntent.RoutineRecommendation or ConsultationIntent.FollowUp
                ? QueryBuilder.CopyFilters(plan.Filters)
                : previous.SearchFilters
        };
        Store(next);
    }

    public void SaveConversation(ConversationState previous, string message)
    {
        Store(previous with
        {
            RecentUserMessages = previous.RecentUserMessages.Append(message).TakeLast(4).ToArray(),
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    public void SaveBudget(ConversationState previous, string message, decimal maximumPriceRials,
        decimal? minimumPriceRials = null)
    {
        Store(previous with
        {
            PendingBudgetRials = maximumPriceRials,
            PendingMinimumBudgetRials = minimumPriceRials,
            RecentUserMessages = previous.RecentUserMessages.Append(message).TakeLast(4).ToArray(),
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private void Store(ConversationState next)
    {
        _cache.Set(next.Id, next,
            new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(30) });
    }

    public static bool IsRepeated(ConversationState state, string question)
    {
        var recent = state.RecentUserMessages.Length > 0 ? state.RecentUserMessages : state.UserQuestions;
        return DateTime.UtcNow - state.UpdatedAtUtc < TimeSpan.FromSeconds(30) && recent.Length >= 3 &&
               recent.TakeLast(3).All(x => x == question);
    }
}
