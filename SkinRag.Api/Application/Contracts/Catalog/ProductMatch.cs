namespace SkinRag.Api.Application.Contracts.Catalog;

public sealed record ProductMatch(ProductDto Product, double Similarity, double Score, string? Reason = null);
