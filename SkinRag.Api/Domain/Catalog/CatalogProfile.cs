namespace SkinRag.Api.Domain.Catalog;

/// <summary>A catalog-defined skin or hair profile used for product matching.</summary>
public sealed class CatalogProfile
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
}
