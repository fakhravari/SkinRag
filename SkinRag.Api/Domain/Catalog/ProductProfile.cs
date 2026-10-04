namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductProfile
{
    public int IdProduct { get; set; }
    public int IdCatalogProfile { get; set; }
    public CatalogProfile CatalogProfile { get; set; } = null!;
}
