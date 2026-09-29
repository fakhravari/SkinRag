using System.Text.Json.Serialization;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Infrastructure.AI;
using SkinRag.Api.Services;
using SkinRag.Api.Prompts;

namespace SkinRag.Api.Application.Intent;

public sealed class IntentModelOutput
{
    [JsonRequired] public string Intent { get; init; } = "";
    [JsonRequired] public double Confidence { get; init; }
}

public interface IIntentClassifier
{
    Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct);
}

public sealed class IntentClassifier(IOllamaClient ollama, IConfiguration configuration, ILogger<IntentClassifier> logger) : IIntentClassifier
{
    public async Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct)
    {
        if (ConversationReplies.GetReply(message) is not null) return new(ConsultationIntent.Greeting, 1, "rules");
        // Obvious unrelated requests stop before any model, SQL or embeddings.
        var normalized = PersianText.Normalize(message);
        if (Has(normalized, "جوک", "لطیفه", "joke", "سیاست", "فوتبال", "برنامه نویسی"))
            return new(ConsultationIntent.OffTopic, 1, "rules");
        try
        {
            var output = await ollama.ChatStructuredAsync<IntentModelOutput>(PipelinePrompts.Intent,
                new { message, previousUserQuestions = previousQuestions.TakeLast(2) }, PipelinePrompts.IntentSchema,
                new("Intent", configuration.GetValue("Consultation:IntentTimeoutSeconds", 12), 80), ct);
            if (!TryValidate(output, configuration.GetValue("Consultation:MinimumIntentConfidence", .65), out var decision))
                return new(ConsultationIntent.Unclear, 0, "invalid-model-output");
            if (decision.Intent == ConsultationIntent.Greeting && ConversationReplies.GetReply(message) is null)
                return new(ConsultationIntent.Unclear, decision.Confidence, "model");
            return decision;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (OperationCanceledException or HttpRequestException or InvalidModelOutputException))
        {
            logger.LogWarning("Intent classifier unavailable ({ErrorType}); using conservative routing", ex.GetType().Name);
            // Unclassified text never falls through to product retrieval.
            return new(ConsultationIntent.Unclear, 0, "classifier-unavailable");
        }
    }

    public static bool TryValidate(IntentModelOutput output, double minimum, out IntentDecision decision)
    {
        decision = new(ConsultationIntent.Unclear, 0, "invalid-model-output");
        if (output.Intent is null || !IntentCodes.TryParse(output.Intent, out var intent) ||
            !double.IsFinite(output.Confidence) || output.Confidence < 0 || output.Confidence > 1) return false;
        decision = new(output.Confidence < minimum ? ConsultationIntent.Unclear : intent, output.Confidence, "model");
        return true;
    }

    private static bool Has(string text, params string[] phrases) => phrases.Any(p => (" " + text + " ").Contains(" " + p + " ", StringComparison.Ordinal));
}
