using System.Text.RegularExpressions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Prompts;

namespace SkinRag.Api.Application.Intent;

public sealed partial class IntentClassifier(IOllamaClient ollama, IConfiguration configuration, ILogger<IntentClassifier> logger) : IIntentClassifier
{
    [GeneratedRegex(@"^قیمت(?: (?:محصول|شناسه))? [0-9]+ (?:چنده|چقدره|چقدر است)$")]
    private static partial Regex PriceReference();
    [GeneratedRegex(@"^(?:ترکیبات|مواد تشکیل دهنده)(?: محصول)? [0-9]+ (?:چیه|چیست|رو بگو)$")]
    private static partial Regex DetailsReference();
    [GeneratedRegex(@"^(?:محصول )?[0-9]+ (?:موجوده|موجود است|موجود هست)$")]
    private static partial Regex AvailabilityReference();

    // Match product domains instead of maintaining a growing list of individual SKUs or categories.
    private static readonly string[] ProductDomainTerms =
    [
        "پوست", "مو", "صورت", "لب", "چشم", "مژه", "ناخن", "دست", "بدن", "آرایش", "آرایشی", "زیبایی", "بهداشتی", "مراقبتی",
        "شامپو", "آبرسان", "ضدآفتاب", "ضد آفتاب", "مرطوب کننده", "شوینده", "بالم", "میسلار", "لوسیون",
        "برنزر", "برانزر", "برق لب", "لیپ گلاس", "بی بی کرم", "پرایمر", "پنکیک", "مداد ابرو", "هایلایتر", "فاندیشن",
        "کف سر", "پوست سر", "خوشبو کننده", "خوشبوکننده", "عطر", "سلولزی", "دستمال", "گوش پاک کن",
        "پوشاک", "لباس", "جوراب", "شال", "پیراهن", "تی شرت", "تبلیغاتی", "نشان", "جوایز", "تجهیزات جانبی",
        "نوشیدنی", "خوراکی", "بسته ترکیبی", "بسته های ترکیبی", "مسواک", "خمیر دندان", "نخ دندان", "نوار بهداشتی",
        "کتاب", "مجله", "بروشور", "کیف", "گوشی", "کیسه", "ضد تعریق", "ضدتعریق"
    ];

    private static readonly string[] AllCatalogCategoryTerms =
    [
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc",
        "\u0628\u0647\u062f\u0627\u0634\u062a\u06cc\u0020\u0648\u0020\u0645\u0631\u0627\u0642\u0628\u062a\u06cc",
        "\u062e\u0648\u0634\u0628\u0648\u0020\u06a9\u0646\u0646\u062f\u0647",
        "\u0633\u0644\u0648\u0644\u0632\u06cc",
        "\u06a9\u0627\u0644\u0627\u06cc\u0020\u062a\u0628\u0644\u06cc\u063a\u0627\u062a\u06cc",
        "\u062e\u0648\u0631\u0627\u06a9\u06cc",
        "\u0645\u062d\u0635\u0648\u0644\u0627\u062a\u0020\u062f\u06cc\u06af\u0631",
        "\u0628\u0633\u062a\u0647\u0020\u0647\u0627\u06cc\u0020\u062a\u0631\u06a9\u06cc\u0628\u06cc",
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc\u0020\u0635\u0648\u0631\u062a",
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc\u0020\u0686\u0634\u0645\u0020\u0648\u0020\u0627\u0628\u0631\u0648",
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc\u0020\u0646\u0627\u062e\u0646",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u062f\u0633\u062a\u0020\u0648\u0020\u0635\u0648\u0631\u062a",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u0645\u0648",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u062f\u0633\u062a\u0020\u0648\u0020\u0646\u0627\u062e\u0646",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u062f\u0647\u0627\u0646\u0020\u0648\u0020\u062f\u0646\u062f\u0627\u0646",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u067e\u0627",
        "\u0639\u0637\u0631",
        "\u062e\u0648\u0634\u0628\u0648\u06a9\u0646\u0646\u062f\u0647\u0020\u0628\u062f\u0646",
        "\u06af\u0648\u0634\u0020\u067e\u0627\u06a9\u0020\u06a9\u0646",
        "\u062f\u0633\u062a\u0645\u0627\u0644\u0020\u06a9\u0627\u063a\u0630\u06cc",
        "\u062f\u0633\u062a\u0645\u0627\u0644\u0020\u0645\u0631\u0637\u0648\u0628",
        "\u0648\u06cc\u0698\u0647\u0020\u0628\u0627\u0646\u0648\u0627\u0646",
        "\u067e\u0648\u0634\u0627\u06a9",
        "\u0645\u062d\u062a\u0648\u06cc\u0020\u0622\u0645\u0648\u0632\u0634\u06cc",
        "\u0646\u0634\u0627\u0646\u0020\u0648\u0020\u062c\u0648\u0627\u06cc\u0632",
        "\u062a\u062c\u0647\u06cc\u0632\u0627\u062a\u0020\u062c\u0627\u0646\u0628\u06cc",
        "\u0646\u0648\u0634\u06cc\u062f\u0646\u06cc",
        "\u0698\u0644\u0020\u0645\u0648",
        "\u0644\u0627\u06a9\u0020\u0648\u0020\u0644\u0627\u06a9\u0020\u067e\u0627\u06a9\u0646",
        "\u0645\u0631\u0637\u0648\u0628\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0648\u0020\u0646\u0631\u0645\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0635\u0648\u0631\u062a",
        "\u067e\u0627\u06a9\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0634\u0648\u06cc\u0646\u062f\u0647\u0020\u0644\u0627\u06cc\u0647\u0020\u0628\u0631\u062f\u0627\u0631",
        "\u0633\u0627\u06cc\u0631\u0020\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u0635\u0648\u0631\u062a",
        "\u0634\u0627\u0645\u067e\u0648",
        "\u0633\u0627\u06cc\u0631",
        "\u0645\u0633\u0648\u0627\u06a9",
        "\u0646\u062e\u0020\u062f\u0646\u062f\u0627\u0646",
        "\u0645\u0631\u0637\u0648\u0628\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0648\u0020\u0646\u0631\u0645\u0020\u06a9\u0646\u0646\u062f\u0647",
        "\u067e\u0627\u06a9\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0648\u0020\u0634\u0648\u06cc\u0646\u062f\u0647",
        "\u0636\u062f\u0020\u062a\u0639\u0631\u06cc\u0642",
        "\u0636\u062f\u0020\u062a\u0631\u06a9\u0020\u067e\u0627",
        "\u067e\u0627\u06a9\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0622\u0631\u0627\u06cc\u0634\u06cc",
        "\u067e\u0627\u06a9\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u062f\u0633\u062a\u0020\u0648\u0020\u0635\u0648\u0631\u062a",
        "\u067e\u0627\u06a9\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0628\u062f\u0646",
        "\u0646\u0648\u0627\u0631\u0020\u0628\u0647\u062f\u0627\u0634\u062a\u06cc",
        "\u067e\u062f\u0020\u0631\u0648\u0632\u0627\u0646\u0647",
        "\u067e\u06cc\u0631\u0627\u0647\u0646\u0020\u0648\u0020\u062a\u06cc\u0020\u0634\u0631\u062a",
        "\u0634\u0627\u0644",
        "\u06a9\u062a\u0627\u0628",
        "\u0645\u062c\u0644\u0647",
        "\u0644\u0648\u062d\u0020\u0641\u0634\u0631\u062f\u0647",
        "\u0628\u0631\u0648\u0634\u0648\u0631",
        "\u06a9\u06cc\u0641",
        "\u06af\u0648\u0634\u06cc",
        "\u06a9\u06cc\u0633\u0647",
        "\u062f\u0641\u062a\u0631\u0686\u0647\u0020\u0641\u0627\u06a9\u062a\u0648\u0631",
        "\u062c\u0648\u0631\u0627\u0628",
        "\u0631\u0698\u0020\u0644\u0628",
        "\u0636\u062f\u0020\u0631\u06cc\u0632\u0634",
        "\u062a\u0642\u0648\u06cc\u062a\u0020\u06a9\u0646\u0646\u062f\u0647\u0020\u0648\u0020\u0646\u0631\u0645\u0020\u06a9\u0646\u0646\u062f\u0647",
        "\u0645\u062d\u0627\u0641\u0638\u0020\u0622\u0641\u062a\u0627\u0628\u0020\u0628\u062f\u0646",
        "\u0633\u0627\u06cc\u0631\u0020\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u0628\u062f\u0646",
        "\u06a9\u0631\u0645\u0020\u067e\u0648\u062f\u0631",
        "\u0636\u062f\u0020\u0686\u0631\u0648\u06a9\u0020\u0648\u0020\u062a\u0642\u0648\u06cc\u062a\u06cc\u0020\u0635\u0648\u0631\u062a",
        "\u0645\u062d\u0627\u0641\u0638\u0020\u0622\u0641\u062a\u0627\u0628",
        "\u0648\u06cc\u0698\u0647\u0020\u0627\u0635\u0644\u0627\u062d",
        "\u062c\u0634\u0646\u0648\u0627\u0631\u0647\u0020\u0634\u06af\u0641\u062a\u0020\u0627\u0646\u06af\u06cc\u0632\u0647\u0627",
        "\u0642\u0644\u0645\u0020\u0645\u0648",
        "\u062e\u0645\u06cc\u0631\u0020\u062f\u0646\u062f\u0627\u0646",
        "\u062f\u0648\u0631\u0020\u0686\u0634\u0645",
        "\u0631\u0698\u06af\u0648\u0646\u0647",
        "\u067e\u0646\u06a9\u06cc\u06a9",
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc\u0020\u0628\u062f\u0646",
        "\u06a9\u0644\u0631\u0628\u0648\u06a9",
        "\u0632\u06cc\u0628\u0627\u06cc\u06cc\u0020\u0645\u0648",
        "\u06a9\u0627\u062a\u0627\u0644\u0648\u06af",
        "\u0645\u0631\u0627\u0642\u0628\u062a\u0020\u0628\u062f\u0646",
        "\u0634\u0627\u0645\u067e\u0648\u0020\u0628\u062f\u0646",
        "\u0646\u0648\u0634\u06cc\u062f\u0646\u06cc\u0020\u0633\u0631\u062f",
        "\u0646\u0648\u0634\u06cc\u062f\u0646\u06cc\u0020\u06af\u0631\u0645",
        "\u0631\u0698\u0644\u0628\u0020\u062c\u0627\u0645\u062f",
        "\u0631\u06cc\u0645\u0644",
        "\u0647\u0627\u0634\u0648\u0631\u0020\u0627\u0628\u0631\u0648",
        "\u0645\u062d\u0635\u0648\u0644\u0627\u062a\u0020\u062e\u0627\u0646\u0648\u0627\u062f\u0647\u0020\u0627\u0646\u0627\u0631",
        "\u0645\u062d\u0635\u0648\u0644\u0627\u062a\u0020\u062e\u0627\u0646\u0648\u0627\u062f\u0647\u0020\u0648\u0627\u06cc\u067e\u0631",
        "\u0645\u062d\u0635\u0648\u0644\u0627\u062a\u0020\u062f\u0631\u0645\u0627\u0645\u06cc\u0644",
        "\u067e\u062f\u0020\u0622\u0631\u0627\u06cc\u0634\u0020\u067e\u0627\u06a9\u200c\u06a9\u0646",
        "\u062a\u06cc\u0646\u062a\u0020\u0622\u0628\u0631\u0633\u0627\u0646",
        "\u0628\u0631\u0633\u0020\u0622\u0631\u0627\u06cc\u0634\u06cc",
        "\u0633\u0627\u06cc\u0647\u0020\u0686\u0634\u0645",
        "\u0642\u0644\u0645\u0020\u0633\u0641\u06cc\u062f\u06a9\u0646\u0646\u062f\u0647\u0020\u062f\u0646\u062f\u0627\u0646",
    ];

    private static readonly string[] ProductRequestTerms =
    [
        "میخوام", "میخواهم", "می خوام", "می خواهم", "معرفی کن", "معرفی کنید", "معرفی میکنی",
        "پیشنهاد بده", "پیشنهاد بدید", "پیشنهاد کنید", "دنبال"
    ];

    private static readonly string[] CustomerConcernRoots =
    [
        "چرب", "خشک", "وز", "ریزش", "شوره", "پوسته", "خارش", "جوش", "لک", "چروک",
        "منافذ", "حساس", "ترک", "تیرگی", "سیاهی", "پف", "قرمز", "نازک", "کم پشت", "کم حجم",
        "فر", "رنگ", "معمولی", "مختلط", "نرمال", "آسیب", "کدر", "دهیدراته", "کم آب"
    ];

    private static readonly string[] ProductFollowUpReferences =
    [
        "این", "اون", "همین", "اولی", "دومی", "سومی", "قبلی", "هم", "بین اینا",
        "قیمتش", "ترکیباتش", "موجوده", "ارزان تر", "ارزون تر"
    ];

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
        // Explicit product requests in a known care domain bypass small-model confidence calibration.
        // Factual price, stock, and ingredient questions continue through normal intent routing.
        if (HasProductDomain(normalized)
            && Has(normalized, ProductRequestTerms)
            && !Has(normalized, "قیمت", "چند", "هزینه", "موجود", "ترکیبات", "مواد تشکیل دهنده", "روش مصرف"))
        {
            return new(ConsultationIntent.ProductSearch, 1, "persian-product-request-rule");
        }
        // A standalone concern (for example, "موهام زود چرب میشه") is a consultation request,
        // even when the customer does not explicitly say "recommend a product".
        if (HasProductDomain(normalized) && normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(word => CustomerConcernRoots.Any(root => word.StartsWith(root, StringComparison.Ordinal)))
            && (!context.HasProductContext || !Has(normalized, ProductFollowUpReferences)))
        {
            return new(ConsultationIntent.SkinConsultation, 1, "customer-concern-rule");
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

    private static bool HasProductDomain(string text)
    {
        if (text.Contains("کف سر", StringComparison.Ordinal))
        {
            return true;
        }

        if (Has(text, ProductDomainTerms) || Has(text, AllCatalogCategoryTerms))
        {
            return true;
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word =>
            word.StartsWith("مو", StringComparison.Ordinal) && word.Length <= 7
            || word.StartsWith("پوست", StringComparison.Ordinal) && word.Length <= 7
            || word.StartsWith("صورت", StringComparison.Ordinal) && word.Length <= 7);
    }
}
