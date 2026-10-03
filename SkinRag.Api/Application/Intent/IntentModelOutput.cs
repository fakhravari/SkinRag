using System.Text.Json.Serialization;

namespace SkinRag.Api.Application.Intent;

public sealed class IntentModelOutput
{
    [JsonRequired] public string Intent { get; init; } = "";

    [JsonRequired] public double Confidence { get; init; }

    [JsonRequired] public string? ConversationTopic { get; init; }

    [JsonRequired] public string? Clarification { get; init; }

    [JsonRequired] public bool RequiresContext { get; init; }
}
