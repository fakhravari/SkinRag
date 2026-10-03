using System.ComponentModel.DataAnnotations;

namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed class CatalogSearchRequest : CatalogFilters
{
    [MaxLength(200)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    public bool InStockOnly { get; set; } = true;
}
