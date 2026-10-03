namespace SkinRag.Api.Domain.Catalog;

public sealed class Brand
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Country { get; set; }
}
