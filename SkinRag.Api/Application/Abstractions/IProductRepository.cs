using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Abstractions;

public interface IProductRepository
{
    Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct);
    Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct);
    Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct);
}
