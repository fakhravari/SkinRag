using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SkinRag.Api.Application.Abstractions;

namespace SkinRag.Api.Application.Common.AI;

public static class StructuredChatCompletion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<T> GetAsync<T>(
        IChatClient chatClient,
        IConfiguration configuration,
        string systemPrompt,
        object input,
        JsonElement schema,
        ModelRequest request,
        CancellationToken cancellationToken) where T : class
    {
        var model = configuration[$"Ollama:{request.Stage}Model"] ?? configuration["Ollama:ChatModel"]
            ?? throw new InvalidOperationException("Ollama:ChatModel is missing.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 70)));

        var fields = schema.TryGetProperty("properties", out var properties)
            ? string.Join(", ", properties.EnumerateObject().Select(property => property.Name))
            : string.Empty;
        var options = new ChatOptions
        {
            ModelId = model,
            Temperature = 0,
            MaxOutputTokens = Math.Clamp(request.MaxTokens, 32, 2000),
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schema)
        };
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties["num_ctx"] = Math.Clamp(configuration.GetValue("Ollama:ContextWindow", 4096),
            2048, 32768);
        options.AdditionalProperties["keep_alive"] = configuration["Ollama:KeepAlive"]
                                                       ?? Environment.GetEnvironmentVariable("OLLAMA_KEEP_ALIVE")
                                                       ?? "30m";
        options.AdditionalProperties["skinrag_stage"] = request.Stage;

        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, systemPrompt + "\nReturn JSON with these fields: " + fields),
            new ChatMessage(ChatRole.User, JsonSerializer.Serialize(input, JsonOptions))
        ], options, timeout.Token);

        if (string.IsNullOrWhiteSpace(response.Text) || response.Text.Length > 16000)
        {
            throw new InvalidModelOutputException("The chat model returned empty or oversized JSON.");
        }

        return Parse<T>(response.Text);
    }

    public static T Parse<T>(string json) where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ??
                   throw new InvalidModelOutputException("The chat model returned null JSON.");
        }
        catch (JsonException)
        {
            throw new InvalidModelOutputException("The chat model output did not match the required JSON contract.");
        }
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new JsonException("Duplicate JSON property.");
                }

                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                ValidateProperties(child);
            }
        }
    }
}
