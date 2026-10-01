namespace SkinRag.Api.Application.Abstractions;

public sealed record ModelRequest(string Stage, int TimeoutSeconds, int MaxTokens);
