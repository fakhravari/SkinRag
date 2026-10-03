namespace SkinRag.Api.Domain.Catalog;

public sealed class Category
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
}
