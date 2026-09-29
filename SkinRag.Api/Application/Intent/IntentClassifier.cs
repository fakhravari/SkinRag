using System.Text.RegularExpressions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Prompts;
using SkinRag.Api.Services;

namespace SkinRag.Api.Application.Intent;

public sealed partial class IntentClassifier(IOllamaClient ollama, IConfiguration configuration, ILogger<IntentClassifier> logger) : IIntentClassifier
{
    [GeneratedRegex(@"^قیمت(?: (?:محصول|شناسه))? [0-9]+ (?:چنده|چقدره|چقدر است)$")]
    private static partial Regex PriceReference();
    [GeneratedRegex(@"^(?:ترکیبات|مواد تشکیل دهنده)(?: محصول)? [0-9]+ (?:چیه|چیست|رو بگو)$")]
    private static partial Regex DetailsReference();
    [GeneratedRegex(@"^(?:محصول )?[0-9]+ (?:موجوده|موجود است|موجود هست)$")]
    private static partial Regex AvailabilityReference();

    [GeneratedRegex(@"^(?:(?:یه|یک|یکی|گزینه|محصول) )?(?:ارزان|ارزون) ?تر(?:ش)?(?: (?:چی|چیه|هم|داری|هست|موجوده|میخوام|میخواهم))*$")]
    private static partial Regex BudgetFollowUp();

    public Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct) =>
        ClassifyAsync(message, new IntentContext(previousQuestions, previousQuestions, previousQuestions.Count > 0), ct);

    public async Task<IntentDecision> ClassifyAsync(string message, IntentContext context, CancellationToken ct)
    {
        if (ConversationReplies.GetTopic(message) is { } topic)
        {
            return new(topic == ConversationTopic.Greeting ? ConsultationIntent.Greeting : ConsultationIntent.SmallTalk, 1, "rules")
            {
                ConversationTopic = topic
            };
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
        if (BudgetFollowUp().IsMatch(normalized))
        {
            shortIntent = ConsultationIntent.FollowUp;
        }
        // Common self-contained Persian product requests should not depend on
        // the local model's confidence calibration.
        if (Has(normalized, "پوست خشک")
            && Has(normalized, "کرم")
            && Has(normalized, "مرطوب کننده", "مرطوبکننده"))
        {
            return new(ConsultationIntent.ProductSearch, 1, "persian-product-rule");
        }
        if (shortIntent.HasValue)
        {
            return new(context.HasProductContext ? shortIntent.Value : ConsultationIntent.Unclear, 1, "context-rule")
            {
                RequiresContext = true,
                Clarification = context.HasProductContext ? null : ClarificationKind.ProductReference
            };
        }

        var budget = BudgetParser.Parse(message);
        if (budget.IsBudgetOnly)
        {
            if (budget.NeedsClarification)
            {
                return new(ConsultationIntent.Unclear, 1, "budget-rule")
                {
                    Clarification = budget.Status == BudgetStatus.MissingCurrency
                        ? ClarificationKind.BudgetCurrency : ClarificationKind.BudgetAmount
                };
            }

            return new(context.HasProductContext ? ConsultationIntent.FollowUp : ConsultationIntent.Unclear, 1, "budget-rule")
            {
                RequiresContext = context.HasProductContext,
                Clarification = context.HasProductContext ? null : ClarificationKind.ProductType
            };
        }

        try
        {
            var output = await ollama.ChatStructuredAsync<IntentModelOutput>(
                PipelinePrompts.Intent,
                new
                {
                    message,
                    recentUserMessages = context.RecentUserMessages.TakeLast(2),
                    productQuestions = context.ProductQuestions.TakeLast(2),
                    hasProductContext = context.HasProductContext
                },
                PipelinePrompts.IntentSchema,
                new(
                "Intent",
                configuration.GetValue("Consultation:IntentTimeoutSeconds", 70),
                configuration.GetValue("Consultation:IntentMaxTokens", 128)),
                ct);
            if (!TryValidate(output, configuration.GetValue("Consultation:MinimumIntentConfidence", .65), out var decision))
            {
                return new(ConsultationIntent.Unclear, 0, "invalid-model-output");
            }

            if ((decision.RequiresContext || decision.Intent == ConsultationIntent.FollowUp) && !context.HasProductContext)
            {
                return decision with
                {
                    Intent = ConsultationIntent.Unclear,
                    ConversationTopic = null,
                    Clarification = ClarificationKind.ProductReference
                };
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

        if (!TryOptionalEnum<ConversationTopic>(output.ConversationTopic, out var topic)
            || !TryOptionalEnum<ClarificationKind>(output.Clarification, out var clarification))
        {
            return false;
        }

        var isSocial = intent is ConsultationIntent.Greeting or ConsultationIntent.SmallTalk;
        if ((intent == ConsultationIntent.SmallTalk && (topic is null or ConversationTopic.Greeting))
            || (intent == ConsultationIntent.Greeting && topic is not (null or ConversationTopic.Greeting))
            || (!isSocial && topic is not null)
            || (isSocial && output.RequiresContext)
            || (intent != ConsultationIntent.Unclear && clarification is not null))
        {
            return false;
        }

        var uncertain = output.Confidence < minimum;
        decision = new(uncertain ? ConsultationIntent.Unclear : intent, output.Confidence, "model")
        {
            ConversationTopic = uncertain ? null : topic,
            Clarification = uncertain ? ClarificationKind.General : clarification,
            RequiresContext = output.RequiresContext
        };
        return true;
    }

    private static bool TryOptionalEnum<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (value is null)
        {
            return true;
        }

        var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        parsed = Enum.Parse<T>(name);
        return true;
    }

    private static bool Has(string text, params string[] phrases) => phrases.Any(p => (" " + text + " ").Contains(" " + p + " ", StringComparison.Ordinal));
}
