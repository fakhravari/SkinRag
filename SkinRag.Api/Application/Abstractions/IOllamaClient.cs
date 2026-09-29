using System.Text.Json;

namespace SkinRag.Api.Application.Abstractions;

public sealed record ModelRequest(string Stage, int TimeoutSeconds, int MaxTokens);

public sealed class InvalidModelOutputException(string message) : Exception(message);

public interface IOllamaClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    Task<T> ChatStructuredAsync<T>(string systemPrompt, object input, JsonElement schema, ModelRequest options, CancellationToken ct) where T : class;
}
