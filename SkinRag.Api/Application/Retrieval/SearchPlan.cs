using SkinRag.Api.Application.Intent;
using SkinRag.Api.Models;

namespace SkinRag.Api.Application.Retrieval;

public sealed record SearchPlan(
        string Query,
        CatalogFilters Filters,
        ConsultationIntent Intent,
        string[] ConcernSlugs,
        bool PreferBudget,
        int[] ProductIds,
        bool InStockOnly = true,
        bool NeedsMoreInformation = false,
        string? FollowUpQuestion = null,
        string Source = "model");
public sealed record CatalogVocabulary(
        Category[] Categories,
        Brand[] Brands,
        Profile[] Profiles,
        Concern[] Concerns,
        Ingredient[] Ingredients);
public sealed record RetrievalResult(
        IReadOnlyList<ProductMatch> Products,
        int EligibleProducts,
        DateTime? IndexUpdatedAtUtc,
        string Method);
public interface IProductRetriever
{
    Task<RetrievalResult> RetrieveAsync(SearchPlan plan, CancellationToken ct);
}
