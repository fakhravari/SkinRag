namespace SkinRag.Api.Infrastructure.Persistence.Entities;

public sealed class ProductEmbedding
{
    public int IdProduct { get; set; }
    public string Model { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int Dimensions { get; set; }
    public string VectorJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}
