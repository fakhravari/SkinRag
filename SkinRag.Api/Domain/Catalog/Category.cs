namespace SkinRag.Api.Domain.Catalog;

public sealed class Category
{
    public int Id { get; set; }
    public int? IdParent { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public Category? Parent { get; set; }
}
