namespace SkinRag.Api.Models;

public sealed class Category
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
}

public sealed class Brand
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Country { get; set; }
}

public sealed class Profile
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
}

public sealed class Concern
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string SearchTerms { get; set; } = "";
}

public sealed class Ingredient
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string InciName { get; set; } = "";
}

public sealed class ProductProfile
{
    public int ProductId { get; set; }
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
}

public sealed class ProductConcern
{
    public int ProductId { get; set; }
    public int ConcernId { get; set; }
    public Concern Concern { get; set; } = null!;
}

public sealed class ProductIngredient
{
    public int ProductId { get; set; }
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
}

public sealed class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal SizeValue { get; set; }
    public string SizeUnit { get; set; } = "";
    public string? Shade { get; set; }
    public string? Finish { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ProductEmbedding
{
    public int ProductId { get; set; }
    public string Model { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int Dimensions { get; set; }
    public string VectorJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}
