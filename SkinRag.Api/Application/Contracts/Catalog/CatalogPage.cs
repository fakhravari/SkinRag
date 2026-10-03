namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed record CatalogPage(int Page, int PageSize, int Total, IReadOnlyList<ProductDto> Items);
