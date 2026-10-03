namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed record CatalogDomainStatistics(string Domain, int Products, int ActiveProducts);

public sealed record CatalogStatistics(
    int TotalProducts,
    int ActiveProducts,
    int AvailableProducts,
    int Variants,
    int Categories,
    int Brands,
    IReadOnlyList<CatalogDomainStatistics> Domains);
