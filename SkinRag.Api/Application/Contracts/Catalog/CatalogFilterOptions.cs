namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed record CatalogCategoryOption(int Id, string Slug, string Name, string Domain, int? ParentId);

public sealed record CatalogBrandOption(int Id, string Slug, string Name, string? Country);

public sealed record CatalogProfileOption(int Id, string Slug, string Name, string Kind);

public sealed record CatalogConcernOption(int Id, string Slug, string Name, string Domain);

public sealed record CatalogIngredientOption(int Id, string Slug, string Name, string InciName);

public sealed record CatalogFilterOptions(
    string Currency,
    string PriceUnit,
    IReadOnlyList<string> Domains,
    IReadOnlyList<CatalogCategoryOption> Categories,
    IReadOnlyList<CatalogBrandOption> Brands,
    IReadOnlyList<CatalogProfileOption> Profiles,
    IReadOnlyList<CatalogConcernOption> Concerns,
    IReadOnlyList<CatalogIngredientOption> Ingredients,
    IReadOnlyList<string> Shades,
    IReadOnlyList<string> Finishes);
