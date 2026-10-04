namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductIngredient
{
    public int IdProduct { get; set; }
    public int IdIngredient { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
}
