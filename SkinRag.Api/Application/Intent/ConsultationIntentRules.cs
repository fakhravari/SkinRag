using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Common.Catalog;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Intent;

internal static class ConsultationIntentRules
{
    private static readonly string[] ProductRequests =
    [
        "معرفی کن", "معرفی کنید", "معرفی میکنی", "پیشنهاد بده", "پیشنهاد بدید", "پیشنهاد کن",
        "پیشنهاد میدی", "چی بخرم", "چی بگیرم", "دنبال", "بخرم", "بگیرم", "نیاز دارم",
        "می خواهم", "می خوام", "میخواهم", "میخوام", "می گردم", "میگردم", "mikham", "mikhaham"
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
        var hasProductRequestLanguage = ContainsAny(normalized, ProductRequests);
        var isExplicitProductRequest = hasProductRequestLanguage
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

        var purchaseWithInformationRequest = matchedProductPhrases.Length > 0
                                             && hasProductRequestLanguage
                                             && ContainsAny(normalized,
                                                 ["می خوام", "میخوام", "می خواهم", "میخواهم", "بخرم", "بگیرم", "دنبال", "mikham", "mikhaham"]);
        if ((isExplicitProductRequest || purchaseWithInformationRequest) && matchedProductPhrases.Length > 0)
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

    public static IntentDecision? MatchGenericHairProductRequest(string message,
        IntentRuleDiagnostics diagnostics)
    {
        if (!diagnostics.HasExplicitProductRequest || diagnostics.MatchedProductPhraseCount > 0)
        {
            return null;
        }

        var normalized = PersianText.Normalize(message);
        var genericHairRequest = HasAny(normalized,
            "چیزی برای مو", "محصول برای مو", "محصول مو", "چیزی واسه مو", "محصول واسه مو");
        return genericHairRequest
            ? new IntentDecision(ConsultationIntent.Unclear, 1, "generic-hair-product-clarification")
            {
                Clarification = ClarificationKind.ProductType
            }
            : null;
    }

    public static bool IsMedicalInfectionRequest(string message)
    {
        // Match inflected Persian forms too (for example "جوش‌های عفونی"),
        // so a potentially infected skin condition never falls through to the
        // intent model when that service is unavailable.
        var tokens = PersianText.NormalizeForMatch(message)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mentionsInfection = tokens.Any(token => token.StartsWith("عفون", StringComparison.Ordinal)
                                                    || token.StartsWith("چرک", StringComparison.Ordinal));
        var mentionsAcne = tokens.Any(token => token.StartsWith("جوش", StringComparison.Ordinal)
                                               || token.StartsWith("آکنه", StringComparison.Ordinal));
        return mentionsInfection && mentionsAcne;
    }

    public static bool IsNoRinseDryShampooRequest(string message)
    {
        var normalized = PersianText.Normalize(message);
        return HasAny(normalized, "شامپو خشک", "شامپوی خشک")
               && HasAny(normalized, "وقت ندارم", "وقتی وقت ندارم", "موهامو بشورم", "بدون آبکشی", "بدون نیاز به آب");
    }

    public static bool IsMoisturizerTextureComparisonRequest(string message)
    {
        var normalized = PersianText.Normalize(message);
        return HasAny(normalized, "کرم مرطوب کننده", "مرطوب کننده کرمی")
               && HasAny(normalized, "ژل مرطوب کننده", "مرطوب کننده ژلی")
               && HasAny(normalized, "بهتره یا", "بهتر است یا", "کدوم بهتر", "کدام بهتر")
               && HasAny(normalized, "پوستم چرب", "پوستم چربه", "پوست چرب");
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

    private static bool HasAny(string text, params string[] phrases) =>
        phrases.Any(phrase => PersianText.ContainsPhrase(text, phrase));
}

internal sealed record IntentRuleDiagnostics(
    bool HasExplicitProductRequest,
    int MatchedProductPhraseCount,
    int MatchedDefinitionPhraseCount,
    string? BestMatchedProductPhrase);
