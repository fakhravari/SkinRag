using System.Text.RegularExpressions;

namespace SkinRag.Api.Application.Validation;

public sealed record GuardResult(bool IsValid, string? Code = null, string? Message = null);
public sealed class InputRejectedException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed partial class InputGuard
{
    [GeneratedRegex(@"(.)\1{19,}")]
    private static partial Regex RepeatedCharacter();

    public GuardResult Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, "EMPTY", "پیام نمی‌تواند خالی باشد.");
        if (text.Length > 2000) return new(false, "TOO_LONG", "پیام باید حداکثر ۲۰۰۰ نویسه باشد.");
        if (!text.Any(char.IsLetterOrDigit) || RepeatedCharacter().IsMatch(text))
            return new(false, "SPAM", "لطفاً یک پرسش خوانا درباره محصولات بنویسید.");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 16 && words.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Max(x => x.Count()) > words.Length * .8)
            return new(false, "SPAM", "پیام شامل تکرار بیش از حد است؛ پرسش را کوتاه‌تر بنویسید.");
        return new(true);
    }
}
