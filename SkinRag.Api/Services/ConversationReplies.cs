using System.Text.RegularExpressions;
using System.Collections.Frozen;
using SkinRag.Api.Infrastructure;

namespace SkinRag.Api.Services;

public static partial class ConversationReplies
{
    private enum Intent { None, Greeting, Wellbeing, Thanks, Farewell, About }

    private static readonly FrozenDictionary<string, Intent> Phrases = new Dictionary<string, Intent>
    {
        ["سلام"] = Intent.Greeting, ["درود"] = Intent.Greeting, ["سلام علیکم"] = Intent.Greeting,
        ["وقت بخیر"] = Intent.Greeting, ["صبح بخیر"] = Intent.Greeting, ["عصر بخیر"] = Intent.Greeting,
        ["روز بخیر"] = Intent.Greeting, ["hello"] = Intent.Greeting, ["hi"] = Intent.Greeting,
        ["خوبی"] = Intent.Wellbeing, ["خوبید"] = Intent.Wellbeing, ["خوبین"] = Intent.Wellbeing,
        ["چطوری"] = Intent.Wellbeing, ["چطورید"] = Intent.Wellbeing, ["حالت چطوره"] = Intent.Wellbeing,
        ["حال شما چطوره"] = Intent.Wellbeing, ["چه خبر"] = Intent.Wellbeing, ["چه خبرا"] = Intent.Wellbeing,
        ["چخبر"] = Intent.Wellbeing, ["خسته نباشی"] = Intent.Wellbeing, ["خسته نباشید"] = Intent.Wellbeing,
        ["ممنون"] = Intent.Thanks, ["مرسی"] = Intent.Thanks, ["متشکرم"] = Intent.Thanks,
        ["خیلی ممنون"] = Intent.Thanks, ["ممنونم"] = Intent.Thanks, ["سپاس"] = Intent.Thanks,
        ["دمت گرم"] = Intent.Thanks, ["دستت درد نکنه"] = Intent.Thanks, ["thanks"] = Intent.Thanks,
        ["خداحافظ"] = Intent.Farewell, ["خدا حافظ"] = Intent.Farewell, ["فعلا"] = Intent.Farewell,
        ["شب بخیر"] = Intent.Farewell, ["بای"] = Intent.Farewell, ["bye"] = Intent.Farewell,
        ["تو کی هستی"] = Intent.About, ["کی هستی"] = Intent.About, ["خودت رو معرفی کن"] = Intent.About,
        ["چه کاری انجام میدی"] = Intent.About, ["چه کار میکنی"] = Intent.About,
        ["عزیزم"] = Intent.None, ["رفیق"] = Intent.None, ["دوست عزیز"] = Intent.None,
        ["دوست من"] = Intent.None, ["داداش"] = Intent.None, ["سلامت باشی"] = Intent.Thanks
    }.ToFrozenDictionary();

    [GeneratedRegex(@"(\p{L})\1{2,}")]
    private static partial Regex RepeatedLetters();

    public static string? GetReply(string question)
    {
        var words = RepeatedLetters().Replace(PersianText.Normalize(question), "$1").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 24) return null;
        var intent = Intent.None;
        // Every word must belong to a conversational phrase; product requests must still use retrieval.
        for (var offset = 0; offset < words.Length;)
        {
            var consumed = 0;
            for (var count = Math.Min(5, words.Length - offset); count > 0; count--)
            {
                if (!Phrases.TryGetValue(string.Join(' ', words, offset, count), out var found)) continue;
                if ((int)found > (int)intent) intent = found;
                consumed = count;
                break;
            }
            if (consumed == 0) return null;
            offset += consumed;
        }
        return intent switch
        {
            Intent.Greeting => "سلام! خوش آمدید 🌿\nبرای پیدا کردن محصولات پوست، مو و زیبایی کمکتان می‌کنم. نیازتان را بنویسید؛ انتخاب فیلترها کاملاً اختیاری است.",
            Intent.Wellbeing => "ممنون از احوال‌پرسی! آماده‌ام کمکتان کنم 🌿\nاگر محصولی برای پوست، مو یا زیبایی می‌خواهید، نیازتان را بنویسید. فیلترها اختیاری‌اند.",
            Intent.Thanks => "خواهش می‌کنم! اگر نیاز دیگری درباره محصولات پوست، مو یا زیبایی دارید، بنویسید.",
            Intent.Farewell => "خدانگهدار! هر زمان درباره محصولات پوست، مو یا زیبایی پرسشی داشتید، اینجا هستم 🌿",
            Intent.About => "من دستیار اطلاعات محصولات پوست، مو و زیبایی هستم. از کاتالوگ و موجودی ثبت‌شده برای پاسخ‌گویی کمک می‌گیرم. کافی است نیازتان را بنویسید؛ فیلترها اختیاری‌اند.",
            _ => null
        };
    }
}
