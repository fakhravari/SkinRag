using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Common.Catalog;

/// <summary>Shared rules for matching catalog phrase mappings across intent and retrieval.</summary>
public static class CatalogPhraseMatcher
{
    public static bool IsProductMapping(CatalogPhrase phrase) =>
        phrase.IsActive && phrase.MappingStatus == CatalogPhraseStatus.Product;

    public static bool IsDefinitionOnlyMapping(CatalogPhrase phrase) =>
        phrase.IsActive && phrase.MappingStatus == CatalogPhraseStatus.DefinitionOnly;

    public static bool Matches(string text, CatalogPhrase phrase, bool includeSearchTerms = false)
    {
        return PersianText.ContainsPhrase(text, phrase.Phrase)
               || (includeSearchTerms && phrase.SearchTerms is { Length: > 0 } terms
                   && PersianText.ContainsPhrase(text, terms));
    }

    /// <summary>
    /// Matches a catalog phrase when its meaningful words appear in the message
    /// with optional customer wording between them, such as "هایلایتر طلایی مات".
    /// Exact matching remains the first choice; the flexible check requires at
    /// least two meaningful phrase words to avoid broad single-word matches.
    /// </summary>
    public static bool MatchesFlexible(string text, CatalogPhrase phrase, bool includeSearchTerms = false)
    {
        if (Matches(text, phrase, includeSearchTerms))
        {
            return true;
        }

        var messageTokens = PersianText.SearchTokens(text);
        if (ContainsAllPhraseTokens(phrase.Phrase, messageTokens))
        {
            return true;
        }

        return includeSearchTerms && phrase.SearchTerms is { Length: > 0 } terms
               && terms.Split([',', ';', '|', '،'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(term => ContainsAllPhraseTokens(term, messageTokens));
    }

    private static bool ContainsAllPhraseTokens(string phrase, IReadOnlySet<string> messageTokens)
    {
        var tokens = PersianText.SearchTokens(phrase);
        return tokens.Count >= 2 && tokens.All(messageTokens.Contains);
    }

    public static IEnumerable<CatalogPhrase> FindProductMappings(string text,
        IEnumerable<CatalogPhrase>? phrases, bool includeSearchTerms = false)
    {
        return (phrases ?? []).Where(phrase => IsProductMapping(phrase)
                                                && Matches(text, phrase, includeSearchTerms));
    }

    public static IEnumerable<CatalogPhrase> FindDefinitionOnlyMappings(string text,
        IEnumerable<CatalogPhrase>? phrases, bool includeSearchTerms = false)
    {
        return (phrases ?? []).Where(phrase => IsDefinitionOnlyMapping(phrase)
                                                && Matches(text, phrase, includeSearchTerms));
    }
}
