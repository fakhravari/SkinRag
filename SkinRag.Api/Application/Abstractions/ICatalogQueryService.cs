using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Abstractions;

public interface ICatalogQueryService
{
    Task<CatalogPage> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken);
    Task<ProductDto?> GetAsync(int id, CancellationToken cancellationToken);
    Task<CatalogFilterOptions> GetFiltersAsync(CancellationToken cancellationToken);
    Task<CatalogStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
}
