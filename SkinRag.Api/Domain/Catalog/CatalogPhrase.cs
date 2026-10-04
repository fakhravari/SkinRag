namespace SkinRag.Api.Domain.Catalog;

/// <summary>A catalog phrase used for category matching or search-term expansion.</summary>
public sealed class CatalogPhrase
{
    public int Id { get; set; }
    public int? IdCategory { get; set; }
    public Category? Category { get; set; }
    public string Phrase { get; set; } = "";
    public string? SearchTerms { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
