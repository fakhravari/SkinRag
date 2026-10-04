using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Retrieval;

/// <summary>Expands reviewed customer wording using mappings loaded from the catalog database.</summary>
public static class CustomerLanguageQuery
{
    public static string ExpandTerms(string customerMessage, IEnumerable<CatalogPhrase>? mappings)
    {
        return string.Join(' ', MatchingTerms(customerMessage, mappings));
    }

    public static string AppendCatalogTerms(string catalogQuery, string customerMessage,
        IEnumerable<CatalogPhrase>? mappings)
    {
        var query = PersianText.Normalize(catalogQuery);
        var additions = MatchingTerms(customerMessage, mappings)
            .Where(term => string.IsNullOrWhiteSpace(query)
                           || !query.Contains(PersianText.Normalize(term), StringComparison.Ordinal));
        return string.Join(' ',
            new[] { catalogQuery, string.Join(' ', additions) }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }

    private static IEnumerable<string> MatchingTerms(string customerMessage,
        IEnumerable<CatalogPhrase>? mappings)
    {
        var text = PersianText.Normalize(customerMessage);
        return (mappings ?? [])
            .Where(mapping => mapping.IsActive
                              && mapping.SearchTerms is not null
                              && text.Contains(PersianText.Normalize(mapping.Phrase), StringComparison.Ordinal))
            .Select(mapping => mapping.SearchTerms!)
            .Where(terms => !string.IsNullOrWhiteSpace(terms))
            .Distinct(StringComparer.Ordinal);
    }
}
