using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SkinRag.Api.Services;

public sealed class OllamaClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public string EmbeddingModel => configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

    public async Task<float[]> EmbedAsync(
        string text, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return (await EmbedBatchAsync([text], timeout.Token))[0];
    }

    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];
        using var httpClient = httpClientFactory.CreateClient("Ollama");
        using var response = await httpClient.PostAsJsonAsync(
            "/api/embed",
            new { model = EmbeddingModel, input = texts, truncate = false },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(
            cancellationToken: cancellationToken);

        if (result?.Embeddings is not { Length: > 0 } embeddings || embeddings.Length != texts.Count ||
            embeddings.Any(v => v is null || v.Length == 0 || v.Any(x => !float.IsFinite(x)) || !v.Any(x => x != 0)))
            throw new InvalidOperationException("Ollama returned invalid embeddings.");

        return embeddings;
    }

    public async Task<string> ChatAsync(
        string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var model = configuration["Ollama:ChatModel"]
            ?? throw new InvalidOperationException("Ollama:ChatModel is missing.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Ollama:ChatTimeoutSeconds", 60), 1, 70)));
        cancellationToken = timeout.Token;

        using var httpClient = httpClientFactory.CreateClient("Ollama");
        using var response = await httpClient.PostAsJsonAsync(
            "/api/chat",
            new
            {
                model,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                options = new
                {
                    temperature = 0.1,
                    num_predict = Math.Clamp(configuration.GetValue("Ollama:MaxResponseTokens", 180), 100, 2000),
                    num_ctx = Math.Clamp(configuration.GetValue("Ollama:ContextWindow", 4096), 2048, 32768)
                }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(result?.Message?.Content))
            throw new InvalidOperationException("Ollama returned an empty response.");
        return result.Message.Content;
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public float[][]? Embeddings { get; set; }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
