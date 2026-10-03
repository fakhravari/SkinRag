namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductConcern
{
    public int ProductId { get; set; }
    public int ConcernId { get; set; }
    public Concern Concern { get; set; } = null!;
}
