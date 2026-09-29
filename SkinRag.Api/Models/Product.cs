namespace SkinRag.Api.Models;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public string? SkinTypes { get; set; }
    public string? Concerns { get; set; }
    public string? Ingredients { get; set; }
    public string? Description { get; set; }
    public string? Warnings { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public string? Sku { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public string? HairTypes { get; set; }
    public string? UsageInstructions { get; set; }
    public string? SearchKeywords { get; set; }
    public bool IsDemo { get; set; }
    public bool? FragranceFree { get; set; }
    public string Currency { get; set; } = "IRR";
    public DateTime UpdatedAtUtc { get; set; }
    public Category? CategoryDetails { get; set; }
    public Brand? BrandDetails { get; set; }
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductProfile> ProductProfiles { get; set; } = [];
    public List<ProductConcern> ProductConcerns { get; set; } = [];
    public List<ProductIngredient> ProductIngredients { get; set; } = [];
}
