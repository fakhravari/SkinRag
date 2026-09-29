using System.ComponentModel.DataAnnotations;

namespace SkinRag.Api.Models;

public class CatalogFilters : IValidatableObject
{
    [RegularExpression("^(skin|hair|beauty)$")]
    public string? Domain { get; set; }

    [MaxLength(80)]
    public string? CategorySlug { get; set; }

    [MaxLength(80)]
    public string? BrandSlug { get; set; }

    [MaxLength(100)]
    public string? SkinType { get; set; }

    [MaxLength(100)]
    public string? HairType { get; set; }

    [MaxLength(80)]
    public string? ConcernSlug { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MaxPrice { get; set; }

    [MaxLength(100)]
    public string? Shade { get; set; }

    [MaxLength(100)]
    public string? Finish { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal? SizeValue { get; set; }

    [RegularExpression("^(ml|g|pcs)$")]
    public string? SizeUnit { get; set; }
    public bool? FragranceFree { get; set; }

    [MaxLength(20)]
    public string[] ExcludeIngredientSlugs { get; set; } = [];

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult("حداقل قیمت نباید از حداکثر بیشتر باشد.", [nameof(MinPrice), nameof(MaxPrice)]);
        }

        if (ExcludeIngredientSlugs is null
            || ExcludeIngredientSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
        {
            yield return new ValidationResult("ترکیبات مستثنا باید با slug معتبر ارسال شوند.", [nameof(ExcludeIngredientSlugs)]);
        }
    }
}

public sealed class CatalogSearchRequest : CatalogFilters
{
    [MaxLength(200)]
    public string? Search { get; set; }

    [Range(1, 100000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
    public bool InStockOnly { get; set; } = true;
}

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
    string? Sku,
    string Name,
    string? Brand,
    string? BrandSlug,
    string? Category,
    string? CategorySlug,
    string? Domain,
    decimal? Price,
    string Currency,
    int StockQuantity,
    bool IsDemo,
    bool? FragranceFree,
    string? SkinTypes,
    string? HairTypes,
    string? Description,
    string? Warnings,
    string? UsageInstructions,
    string[] Profiles,
    string[] Concerns,
    string[] Ingredients,
    IReadOnlyList<VariantDto> Variants,
    string? IngredientsText = null,
    string? ConcernsText = null);

public sealed record ProductMatch(ProductDto Product, double Similarity, double Score, string? Reason = null);

public sealed record ProductRecommendation(int ProductId, string Reason);

public sealed record ConsultationResponse(
    string Answer,
    IReadOnlyList<ProductMatch> Products,
    string Currency,
    string RetrievalMethod,
    int EligibleProducts,
    DateTime? IndexUpdatedAtUtc,
    bool IsDemo,
    string ResponseMode = "model",
    string? Notice = null,
    string Intent = "PRODUCT_SEARCH",
    double IntentConfidence = 1,
    Guid? ConversationId = null,
    bool NeedsMoreInformation = false,
    string? FollowUpQuestion = null,
    IReadOnlyList<ProductRecommendation>? Recommendations = null);

public sealed record CatalogPage(int Page, int PageSize, int Total, IReadOnlyList<ProductDto> Items);
