using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Retrieval;

public sealed record SearchPlan(
    string Query,
    CatalogFilters Filters,
    ConsultationIntent Intent,
    string[] InferredConcernSlugs,
    bool PreferBudget,
    int[] ProductIds,
    bool InStockOnly = true,
    bool NeedsMoreInformation = false,
    string? FollowUpQuestion = null,
    string Source = "model",
    string[]? InferredProfileSlugs = null,
    string? Notice = null);

public sealed record CatalogVocabulary(
    Category[] Categories,
    Brand[] Brands,
    CatalogProfile[] CatalogProfiles,
    Concern[] Concerns,
    Ingredient[] Ingredients,
    string[]? Shades = null,
    string[]? Finishes = null,
    CatalogPhrase[]? CatalogPhrases = null);

public sealed record RetrievalResult(
    IReadOnlyList<ProductMatch> Products,
    int EligibleProducts,
    DateTime? IndexUpdatedAtUtc,
    string Method,
    double SqlFilterMs = 0,
    double EmbeddingMs = 0,
    double ProductLoadMs = 0,
    int[]? EligibleProductIds = null,
    RetrievalDiagnostics? Diagnostics = null,
    SearchPlan? EffectivePlan = null);

public sealed record RetrievalDiagnostics(
    string Stage,
    string Cause,
    string? FirstRestoringFilter = null,
    ZeroResultFilterDiagnosis? FilterDiagnosis = null,
    int? EligibleCount = null,
    int? IndexCandidateCount = null,
    int? SimilarityCandidateCount = null,
    int? LiveProductCount = null);

public sealed record FilterRelaxationCount(string Filter, int RemainingProducts);

public sealed record ZeroResultFilterDiagnosis(
    IReadOnlyList<FilterRelaxationCount> IndividualRelaxations,
    IReadOnlyList<FilterRelaxationCount> CumulativeRelaxations);

public interface IProductRetriever
{
    Task<RetrievalResult> RetrieveAsync(SearchPlan plan, CancellationToken ct);
}
