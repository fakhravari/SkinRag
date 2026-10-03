namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed record VariantDto(
    int Id,
    string Sku,
    string Name,
    decimal SizeValue,
    string SizeUnit,
    string? Shade,
    string? Finish,
    decimal Price,
    int StockQuantity);

public sealed record ProductDto(
    int Id,
    string Sku,
    string Name,
    string Brand,
    string? BrandSlug,
    string Category,
    string? CategorySlug,
    string? Domain,
    decimal? Price,
    string Currency,
    int StockQuantity,
    bool? FragranceFree,
    string SkinTypes,
    string HairTypes,
    string Description,
    string Warnings,
    string UsageInstructions,
    string Image,
    string[] Profiles,
    string[] Concerns,
    string[] Ingredients,
    IReadOnlyList<VariantDto> Variants,
    string? IngredientsText = null,
    string? ConcernsText = null);
