using System.Text.Json.Serialization;

namespace SkinRag.Api.Application.Retrieval;

public sealed class QueryModelOutput
{
    [JsonRequired] public string Query { get; init; } = "";

    [JsonRequired] public string? Domain { get; init; }

    [JsonRequired] public string? SkinType { get; init; }

    [JsonRequired] public string? HairType { get; init; }

    [JsonRequired] public string? CategorySlug { get; init; }

    [JsonRequired] public string? BrandSlug { get; init; }

    [JsonRequired] public string? Shade { get; init; }

    [JsonRequired] public string? Finish { get; init; }

    [JsonRequired] public string[] ConcernSlugs { get; init; } = [];

    [JsonRequired] public string[] ExcludeIngredientSlugs { get; init; } = [];

    [JsonRequired] public string[] IncludeIngredientSlugs { get; init; } = [];

    [JsonRequired] public bool? FragranceFree { get; init; }

    [JsonRequired] public string PricePreference { get; init; } = "neutral";
}
