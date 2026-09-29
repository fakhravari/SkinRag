using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using SkinRag.Api.Infrastructure.AI;

namespace SkinRag.Api.Services;

public sealed class OllamaClient(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IOllamaClient
{
    public static readonly JsonSerializerOptions StructuredJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
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

    public async Task<T> ChatStructuredAsync<T>(string systemPrompt, object input, JsonElement schema,
        ModelRequest options, CancellationToken cancellationToken) where T : class
    {
        var model = configuration[$"Ollama:{options.Stage}Model"] ?? configuration["Ollama:ChatModel"]
            ?? throw new InvalidOperationException("Ollama:ChatModel is missing.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 70)));
        cancellationToken = timeout.Token;

        using var httpClient = httpClientFactory.CreateClient("Ollama");
        using var response = await httpClient.PostAsJsonAsync(
            "/api/chat",
            new
            {
                model,
                stream = false,
                format = schema,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt + "\nOutput JSON schema:\n" + schema.GetRawText() },
                    new { role = "user", content = JsonSerializer.Serialize(input, StructuredJsonOptions) }
                },
                options = new
                {
                    temperature = 0,
                    num_predict = Math.Clamp(options.MaxTokens, 32, 2000),
                    num_ctx = Math.Clamp(configuration.GetValue("Ollama:ContextWindow", 4096), 2048, 32768)
                }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(result?.Message?.Content) || result.Message.Content.Length > 16000)
            throw new InvalidModelOutputException("Ollama returned empty or oversized JSON.");
        return ParseStructured<T>(result.Message.Content);
    }

    public static T ParseStructured<T>(string json) where T : class
    {
        try
        {
            // Reject duplicate JSON properties as well as missing/unknown fields.
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(json, StructuredJsonOptions)
                ?? throw new InvalidModelOutputException("Ollama returned null JSON.");
        }
        catch (JsonException)
        {
            throw new InvalidModelOutputException("Ollama output did not match the required JSON contract.");
        }
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException("Duplicate JSON property.");
                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateProperties(child);
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
