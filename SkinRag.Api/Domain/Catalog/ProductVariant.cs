namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductVariant
{
    public int Id { get; set; }
    public int IdProduct { get; set; }
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
