using System.ComponentModel.DataAnnotations;

namespace SkinRag.Api.Application.Contracts.Catalog;

public class CatalogFilters : IValidatableObject
{
    [RegularExpression(
        "^(skin|hair|beauty|personal-care|fragrance|cellulose|promotional|food|other|bundles|campaigns)$")]
    public string? Domain { get; set; }

    [MaxLength(20)] public string[] Domains { get; set; } = [];

    [MaxLength(80)] public string? CategorySlug { get; set; }
    [MaxLength(20)] public string[] CategorySlugs { get; set; } = [];
    [MaxLength(80)] public string? BrandSlug { get; set; }
    [MaxLength(20)] public string[] BrandSlugs { get; set; } = [];
    [MaxLength(20)] public string[] ExcludedBrandSlugs { get; set; } = [];
    [MaxLength(100)] public string? SkinType { get; set; }
    [MaxLength(20)] public string[] SkinTypes { get; set; } = [];
    [MaxLength(100)] public string? HairType { get; set; }
    [MaxLength(20)] public string[] HairTypes { get; set; } = [];
    [MaxLength(80)] public string? ConcernSlug { get; set; }
    [MaxLength(20)] public string[] ConcernSlugs { get; set; } = [];

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MaxPrice { get; set; }

    [MaxLength(100)] public string? Shade { get; set; }
    [MaxLength(20)] public string[] Shades { get; set; } = [];
    [MaxLength(100)] public string? Finish { get; set; }
    [MaxLength(20)] public string[] Finishes { get; set; } = [];

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal? SizeValue { get; set; }

    [RegularExpression("^(ml|g|pcs)$")] public string? SizeUnit { get; set; }
    public bool? FragranceFree { get; set; }
    [MaxLength(20)] public string[] ExcludeIngredientSlugs { get; set; } = [];
    [MaxLength(20)] public string[] IncludeIngredientSlugs { get; set; } = [];

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice > MaxPrice)
        {
            yield return new ValidationResult("حداقل قیمت نباید از حداکثر بیشتر باشد.",
                [nameof(MinPrice), nameof(MaxPrice)]);
        }

        if (ExcludeIngredientSlugs is null
            || ExcludeIngredientSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
        {
            yield return new ValidationResult("ترکیبات مستثنا باید با slug معتبر ارسال شوند.",
                [nameof(ExcludeIngredientSlugs)]);
        }

        if (IncludeIngredientSlugs is null
            || IncludeIngredientSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
        {
            yield return new ValidationResult("ترکیبات درخواستی باید با slug معتبر ارسال شوند.",
                [nameof(IncludeIngredientSlugs)]);
        }

        if (Domains is null || Domains.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)
            || CategorySlugs is null || CategorySlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)
            || BrandSlugs is null || BrandSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)
            || ExcludedBrandSlugs is null || ExcludedBrandSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)
            || SkinTypes is null || SkinTypes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100)
            || HairTypes is null || HairTypes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100)
            || ConcernSlugs is null || ConcernSlugs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)
            || Shades is null || Shades.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100)
            || Finishes is null || Finishes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100))
        {
            yield return new ValidationResult("یکی از گزینه‌های انتخابی فیلتر معتبر نیست.");
        }
    }
}
