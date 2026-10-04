namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductConcern
{
    public int IdProduct { get; set; }
    public int IdConcern { get; set; }
    public Concern Concern { get; set; } = null!;
}
