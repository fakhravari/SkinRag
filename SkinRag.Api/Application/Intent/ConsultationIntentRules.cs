using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Intent;

internal static class ConsultationIntentRules
{
    private static readonly string[] ProductRequests =
    [
        "معرفی کن", "معرفی کنید", "معرفی میکنی", "پیشنهاد بده", "پیشنهاد بدید", "پیشنهاد کن",
        "پیشنهاد میدی", "چی بخرم", "چی بگیرم", "دنبال محصول", "میخوام محصول", "میخوام کرم",
        "میخوام شامپو", "محصول میخوام", "محصول میخواهم", "بخرم", "میخوام", "میخواهم"
    ];

    private static readonly string[] InformationalRequests =
    ["قیمت", "چنده", "موجود", "ترکیبات", "مواد تشکیل دهنده", "روش مصرف", "چرا", "علت", "دلیل"];

    private static readonly string[] ProductTerms =
    ["کرم", "مرطوب کننده", "آبرسان", "سرم", "شامپو", "محصول", "ضد آفتاب", "رژ", "ماسک"];

    private static readonly string[] ProductReferences =
    ["این", "اون", "همین", "اولی", "دومی", "سومی", "قبلی", "قیمتش", "ترکیباتش"];

    private static readonly string[] DefinitionRequests =
    ["چیست", "چیه", "یعنی چه", "یعنی چی", "منظور از", "تعریف"];

    public static IntentDecision? Match(string message, IReadOnlyList<Concern> concerns,
        IntentContext context)
    {
        var normalized = PersianText.Normalize(message);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var isExplicitProductRequest = (ContainsAny(normalized, ProductRequests)
                                        || ContainsAny(normalized, ["می خواهم", "می خوام"]))
                                       && !ContainsAny(normalized, InformationalRequests);

        var matched = concerns
            .Where(x => x.Domain is "skin" or "hair"
                        && (PhraseMatches(tokens, x.Name) || PhraseMatches(tokens, x.SearchTerms)))
            .OrderByDescending(x => PersianText.SearchTokens(x.Name).Count)
            .ToArray();
        if (isExplicitProductRequest && ContainsAny(normalized, ProductTerms))
        {
            return new IntentDecision(ConsultationIntent.ProductSearch, 1, "catalog-product-request-rule");
        }

        if (matched.Length == 0)
        {
            return null;
        }

        if (isExplicitProductRequest)
        {
            return new IntentDecision(ConsultationIntent.ProductSearch, 1, "catalog-consultation-request-rule");
        }

        if (ContainsAny(normalized, DefinitionRequests))
        {
            return null;
        }

        if (context.HasProductContext && ContainsAny(normalized, ProductReferences))
        {
            return null;
        }

        return new IntentDecision(ConsultationIntent.SkinConsultation, 1, "catalog-concern-rule");
    }

    private static bool PhraseMatches(IReadOnlyList<string> messageTokens, string phrase)
    {
        var phraseTokens = PersianText.Normalize(phrase).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return phraseTokens.Length > 0 && phraseTokens.All(expected =>
            messageTokens.Any(actual => actual.Equals(expected, StringComparison.Ordinal)
                                        || actual.StartsWith(expected, StringComparison.Ordinal)));
    }

    private static bool ContainsAny(string text, IEnumerable<string> phrases)
    {
        var padded = " " + text + " ";
        return phrases.Any(phrase => padded.Contains(" " + PersianText.Normalize(phrase) + " ",
            StringComparison.Ordinal));
    }
}
