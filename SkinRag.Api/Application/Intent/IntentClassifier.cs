using SkinRag.Api.Infrastructure;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Services;
using SkinRag.Api.Prompts;
using System.Text.RegularExpressions;

namespace SkinRag.Api.Application.Intent;

public sealed partial class IntentClassifier(IOllamaClient ollama, IConfiguration configuration, ILogger<IntentClassifier> logger) : IIntentClassifier
{
    [GeneratedRegex(@"^قیمت(?: (?:محصول|شناسه))? [0-9]+ (?:چنده|چقدره|چقدر است)$")]
    private static partial Regex PriceReference();
    [GeneratedRegex(@"^(?:ترکیبات|مواد تشکیل دهنده)(?: محصول)? [0-9]+ (?:چیه|چیست|رو بگو)$")]
    private static partial Regex DetailsReference();
    [GeneratedRegex(@"^(?:محصول )?[0-9]+ (?:موجوده|موجود است|موجود هست)$")]
    private static partial Regex AvailabilityReference();
    public async Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct)
    {
        if (ConversationReplies.GetReply(message) is not null)
        {
            return new(ConsultationIntent.Greeting, 1, "rules");
        }

        // Obvious unrelated requests stop before any model, SQL or embeddings.
        var normalized = PersianText.Normalize(message);
        if (Has(normalized, "جوک", "لطیفه", "joke", "سیاست", "فوتبال", "برنامه نویسی"))
        {
            return new(ConsultationIntent.OffTopic, 1, "rules");
        }

        if (PriceReference().IsMatch(normalized))
        {
            return new(ConsultationIntent.PriceInquiry, 1, "product-reference-rule");
        }

        if (DetailsReference().IsMatch(normalized))
        {
            return new(ConsultationIntent.ProductDetails, 1, "product-reference-rule");
        }

        if (AvailabilityReference().IsMatch(normalized))
        {
            return new(ConsultationIntent.AvailabilityInquiry, 1, "product-reference-rule");
        }

        var shortIntent = normalized switch
        {
            "قیمتش چنده" or "قیمتش چقدره" or "قیمتش چند" => ConsultationIntent.PriceInquiry,
            "موجوده" or "موجود هست" or "موجود است" => ConsultationIntent.AvailabilityInquiry,
            "ترکیباتش چیه" or "ترکیباتش چیست" or "روش مصرفش چیه" => ConsultationIntent.ProductDetails,
            _ => (ConsultationIntent?)null
        };
        if (shortIntent.HasValue)
        {
            return new(previousQuestions.Count > 0 ? shortIntent.Value : ConsultationIntent.Unclear, 1, "context-rule");
        }

        try
        {
            var output = await ollama.ChatStructuredAsync<IntentModelOutput>(
                                PipelinePrompts.Intent,
                                new
                                {
                                    message,
                                    previousUserQuestions = previousQuestions.TakeLast(2)
                                },
                                PipelinePrompts.IntentSchema,
                                new(
                        "Intent",
                        configuration.GetValue("Consultation:IntentTimeoutSeconds", 70),
                        configuration.GetValue("Consultation:IntentMaxTokens", 64)),
                                ct);
            if (!TryValidate(output, configuration.GetValue("Consultation:MinimumIntentConfidence", .65), out var decision))
            {
                return new(ConsultationIntent.Unclear, 0, "invalid-model-output");
            }

            if (decision.Intent == ConsultationIntent.Greeting && ConversationReplies.GetReply(message) is null)
            {
                return new(ConsultationIntent.Unclear, decision.Confidence, "model");
            }

            return decision;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested

            && ex is (OperationCanceledException or HttpRequestException or InvalidModelOutputException))
        {
            logger.LogWarning("Intent classifier unavailable ({ErrorType}); using conservative routing", ex.GetType().Name);
            // Unclassified text never falls through to product retrieval.
            return new(ConsultationIntent.Unclear, 0, "classifier-unavailable");
        }
    }

    public static bool TryValidate(IntentModelOutput output, double minimum, out IntentDecision decision)
    {
        decision = new(ConsultationIntent.Unclear, 0, "invalid-model-output");
        if (output.Intent is null || !IntentCodes.TryParse(output.Intent, out var intent)

            || !double.IsFinite(output.Confidence)

            || output.Confidence < 0

            || output.Confidence > 1)
        {
            return false;
        }

        decision = new(output.Confidence < minimum ? ConsultationIntent.Unclear : intent, output.Confidence, "model");
        return true;
    }

    private static bool Has(string text, params string[] phrases) => phrases.Any(p => (" " + text + " ").Contains(" " + p + " ", StringComparison.Ordinal));
}
