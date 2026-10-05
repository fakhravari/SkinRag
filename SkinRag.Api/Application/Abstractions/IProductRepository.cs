using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Abstractions;

public interface IProductRepository
{
    Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct);

    Task<IReadOnlyList<CatalogPhrase>> IntentPhrasesAsync(CancellationToken ct);
    Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct);
    Task<ZeroResultFilterDiagnosis> DiagnoseZeroResultsAsync(SearchPlan plan, CancellationToken ct);
    Task<IReadOnlyDictionary<int, int>> CountConcernMatchesAsync(int[] productIds, string[] concernSlugs,
        CancellationToken ct);
    Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct);
}
