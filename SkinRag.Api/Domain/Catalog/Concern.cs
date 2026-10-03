namespace SkinRag.Api.Domain.Catalog;

public sealed class Concern
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string SearchTerms { get; set; } = "";
}
