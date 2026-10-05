namespace SkinRag.Api.Domain.Catalog;

/// <summary>A customer phrase mapped to a product target or retained as a definition-only entry.</summary>
public sealed class CatalogPhrase
{
    public int Id { get; set; }
    public int? IdCategory { get; set; }
    public Category? Category { get; set; }
    public int? IdConcern { get; set; }
    public Concern? Concern { get; set; }
    public int? IdCatalogProfile { get; set; }
    public CatalogProfile? CatalogProfile { get; set; }
    public string Phrase { get; set; } = "";
    public string? SearchTerms { get; set; }
    public string MappingStatus { get; set; } = CatalogPhraseStatus.Product;
    public string? Definition { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
