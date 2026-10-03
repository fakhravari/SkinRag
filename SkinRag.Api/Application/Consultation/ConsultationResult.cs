using System.Text.Json.Serialization;
using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Consultation;

public sealed class ConsultationResult
{
    [JsonRequired] public string Answer { get; init; } = "";

    [JsonRequired] public List<ProductRecommendation> Recommendations { get; init; } = [];

    [JsonRequired] public bool NeedsMoreInformation { get; init; }

    [JsonRequired] public string? FollowUpQuestion { get; init; }
}
