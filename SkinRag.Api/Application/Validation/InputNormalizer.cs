using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SkinRag.Api.Application.Validation;

public static partial class InputNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"([!?؟])\1+")]
    private static partial Regex RepeatedPunctuation();

    // Preserve punctuation, wording and case; search token normalization is a separate operation.
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var text = new StringBuilder(input.Length);
        foreach (var c in input.Normalize(NormalizationForm.FormKC))
        {
            if (c == '\u0640' || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= '۰' and <= '۹') text.Append((char)('0' + c - '۰'));
            else if (c is >= '٠' and <= '٩') text.Append((char)('0' + c - '٠'));
            else if (char.IsControl(c) || c is '\u200c' or '\u200d') text.Append(' ');
            else if (char.GetUnicodeCategory(c) != UnicodeCategory.Format)
                text.Append(c switch { 'ك' => 'ک', 'ي' or 'ى' => 'ی', _ => c });
        }
        return RepeatedPunctuation().Replace(Spaces().Replace(text.ToString(), " ").Trim(), "$1");
    }
}
