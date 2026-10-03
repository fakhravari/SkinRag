namespace SkinRag.Api.Domain.Catalog;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "بدون برند";
    public string Category { get; set; } = "";
    public string SkinTypes { get; set; } = "نامشخص";
    public string Concerns { get; set; } = "نامشخص";
    public string Ingredients { get; set; } = "اطلاعات ترکیبات ثبت نشده";
    public string Description { get; set; } = "اطلاعات توضیحات ثبت نشده";
    public string Warnings { get; set; } = "اطلاعات هشدار ثبت نشده";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public string Sku { get; set; } = "";
    public int CategoryId { get; set; }
    public int BrandId { get; set; }
    public string HairTypes { get; set; } = "نامشخص";
    public string UsageInstructions { get; set; } = "اطلاعات روش مصرف ثبت نشده";
    public string SearchKeywords { get; set; } = "";
    public string Image { get; set; } = "";
    public bool FragranceFree { get; set; }
    public bool FragranceFreeKnown { get; set; }
    public string Currency { get; set; } = "IRR";
    public DateTime UpdatedAtUtc { get; set; }
    public Category? CategoryDetails { get; set; }
    public Brand? BrandDetails { get; set; }
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductProfile> ProductProfiles { get; set; } = [];
    public List<ProductConcern> ProductConcerns { get; set; } = [];
    public List<ProductIngredient> ProductIngredients { get; set; } = [];
}
