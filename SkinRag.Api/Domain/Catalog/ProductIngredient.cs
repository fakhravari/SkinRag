namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductIngredient
{
    public int ProductId { get; set; }
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
}
