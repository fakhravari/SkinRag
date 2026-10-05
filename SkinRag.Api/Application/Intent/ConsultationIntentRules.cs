using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Common.Catalog;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Intent;

internal static class ConsultationIntentRules
{
    private static readonly string[] ProductRequests =
    [
        "معرفی کن", "معرفی کنید", "معرفی میکنی", "پیشنهاد بده", "پیشنهاد بدید", "پیشنهاد کن",
        "پیشنهاد میدی", "چی بخرم", "چی بگیرم", "دنبال محصول", "بخرم", "بگیرم", "نیاز دارم",
        "می خواهم", "می خوام", "میخواهم", "میخوام"
    ];
    private static readonly string[] InformationalRequests =
    ["قیمت", "چنده", "موجود", "ترکیبات", "مواد تشکیل دهنده", "روش مصرف", "چرا", "علت", "دلیل"];

    private static readonly string[] ProductReferences =
    ["این", "اون", "همین", "اولی", "دومی", "سومی", "قبلی", "قیمتش", "ترکیباتش"];

    private static readonly string[] DefinitionRequests =
    ["چیست", "چیه", "یعنی چه", "یعنی چی", "منظور از", "تعریف"];

    public static IntentDecision? Match(string message, IReadOnlyList<CatalogPhrase> phrases,
        IntentContext context, out IntentRuleDiagnostics diagnostics)
    {
        var normalized = PersianText.Normalize(message);
        var isExplicitProductRequest = ContainsAny(normalized, ProductRequests)
                                       && !ContainsAny(normalized, InformationalRequests);

        var matchedDefinitions = phrases.Where(CatalogPhraseMatcher.IsDefinitionOnlyMapping)
            .Where(phrase => MatchesForIntent(message, phrase))
            .ToArray();
        var matchedProductPhrases = phrases.Where(CatalogPhraseMatcher.IsProductMapping)
            .Where(phrase => MatchesForIntent(message, phrase))
            .OrderByDescending(x => PersianText.SearchTokens(x.Phrase).Count)
            .ToArray();
        diagnostics = new IntentRuleDiagnostics(
            isExplicitProductRequest,
            matchedProductPhrases.Length,
            matchedDefinitions.Length,
            matchedProductPhrases.FirstOrDefault()?.Phrase);
        var matched = matchedProductPhrases.Where(x => x.Concern is not null).ToArray();
        if (isExplicitProductRequest && matchedDefinitions.Length > 0 && matchedProductPhrases.Length == 0)
        {
            return new IntentDecision(ConsultationIntent.Unclear, 1, "definition-only-catalog-phrase");
        }

        if (isExplicitProductRequest && matchedProductPhrases.Length > 0)
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

    private static bool MatchesForIntent(string message, CatalogPhrase phrase)
    {
        return CatalogPhraseMatcher.MatchesFlexible(message, phrase, includeSearchTerms: true);
    }

    private static bool ContainsAny(string text, IEnumerable<string> phrases)
    {
        var padded = " " + text + " ";
        return phrases.Any(phrase => PersianText.ContainsPhrase(padded, phrase));
    }
}

internal sealed record IntentRuleDiagnostics(
    bool HasExplicitProductRequest,
    int MatchedProductPhraseCount,
    int MatchedDefinitionPhraseCount,
    string? BestMatchedProductPhrase);
