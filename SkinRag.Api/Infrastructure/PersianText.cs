using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace SkinRag.Api.Infrastructure;

public static class PersianText
{
    private static readonly FrozenSet<string> StopWords = "برای من یک و با به از در که چه می میخوام میخواهم دارم محصول محصولات لطفا".Split(' ').ToFrozenSet();

    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var original in text.Normalize(NormalizationForm.FormKC))
        {
            var c = original switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                _ => char.ToLowerInvariant(original)
            };
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
            }
            else if (result.Length > 0 && result[^1] != ' ')
            {
                result.Append(' ');
            }
        }

        return result.ToString().TrimEnd();
    }

    public static FrozenSet<string> SearchTokens(string text) => Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 1 && !StopWords.Contains(word))
        .ToFrozenSet();
}
