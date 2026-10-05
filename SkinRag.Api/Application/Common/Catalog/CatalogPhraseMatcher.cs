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
