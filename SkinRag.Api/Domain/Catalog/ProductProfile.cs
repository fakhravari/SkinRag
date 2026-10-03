namespace SkinRag.Api.Domain.Catalog;

public sealed class ProductProfile
{
    public int ProductId { get; set; }
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
}
