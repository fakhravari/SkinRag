using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace SkinRag.Api.Application.Common.Text;

public static class PersianText
{
    private static readonly string[] JoinableSuffixes = ["کننده", "کنندگان", "ترین", "تر", "های", "ها"];

    private static readonly FrozenSet<string> StopWords =
        "برای من یک و با به از در که چه می میخوام میخواهم دارم محصول محصولات لطفا".Split(' ').ToFrozenSet();

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

    /// <summary>Normalizes text for phrase matching while retaining meaningful word boundaries.</summary>
    public static string NormalizeForMatch(string text)
    {
        var normalized = Normalize(text);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if ((tokens[i] is "می" or "نمی") && i + 1 < tokens.Count)
            {
                tokens[i] += tokens[i + 1];
                tokens.RemoveAt(i + 1);
                continue;
            }

            if (i > 0 && JoinableSuffixes.Any(suffix => tokens[i].StartsWith(suffix, StringComparison.Ordinal)
                                                        && tokens[i - 1].Length > 1))
            {
                tokens[i - 1] += tokens[i];
                tokens.RemoveAt(i);
                i--;
            }
        }

        return string.Join(' ', tokens);
    }

    public static bool ContainsPhrase(string text, string phrase)
    {
        var normalizedText = NormalizeForMatch(text);
        var normalizedPhrase = NormalizeForMatch(phrase);
        if (normalizedPhrase.Length == 0)
        {
            return false;
        }

        if ((" " + normalizedText + " ").Contains(" " + normalizedPhrase + " ", StringComparison.Ordinal))
        {
            return true;
        }

        var textTokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var phraseTokens = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (phraseTokens.Length > textTokens.Length)
        {
            return false;
        }

        for (var start = 0; start <= textTokens.Length - phraseTokens.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < phraseTokens.Length; offset++)
            {
                if (textTokens[start + offset] == phraseTokens[offset]
                    || IsEzafeVariant(textTokens[start + offset], phraseTokens[offset]))
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Matches a token with the attached Persian ezafe yeh after a long vowel.</summary>
    public static bool SearchTokenMatches(string candidate, string expected)
    {
        return candidate == expected || IsEzafeVariant(candidate, expected);
    }

    private static bool IsEzafeVariant(string candidate, string expected)
    {
        return expected.Length >= 2
               && candidate.Length == expected.Length + 1
               && candidate.StartsWith(expected, StringComparison.Ordinal)
               && candidate[^1] == 'ی'
               && expected[^1] is 'ا' or 'آ' or 'و';
    }

    public static FrozenSet<string> SearchTokens(string text)
    {
        return Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && !StopWords.Contains(word))
            .ToFrozenSet();
    }
}
