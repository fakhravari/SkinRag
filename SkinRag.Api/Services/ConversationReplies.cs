using System.Collections.Frozen;
using System.Text.RegularExpressions;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Infrastructure;

namespace SkinRag.Api.Services;

public static partial class ConversationReplies
{
    private enum Intent
    {
        None,
        Greeting,
        Wellbeing,
        Thanks,
        Farewell,
        About,
        Location,
        DailyPlans
    }

    private static readonly FrozenDictionary<string, Intent> Phrases = new Dictionary<string, Intent>
    {
        ["سلام"] = Intent.Greeting,
        ["درود"] = Intent.Greeting,
        ["سلام علیکم"] = Intent.Greeting,
        ["وقت بخیر"] = Intent.Greeting,
        ["صبح بخیر"] = Intent.Greeting,
        ["عصر بخیر"] = Intent.Greeting,
        ["روز بخیر"] = Intent.Greeting,
        ["hello"] = Intent.Greeting,
        ["hi"] = Intent.Greeting,
        ["خوبی"] = Intent.Wellbeing,
        ["خوبید"] = Intent.Wellbeing,
        ["خوبین"] = Intent.Wellbeing,
        ["چطوری"] = Intent.Wellbeing,
        ["چطورید"] = Intent.Wellbeing,
        ["حالت چطوره"] = Intent.Wellbeing,
        ["حال شما چطوره"] = Intent.Wellbeing,
        ["چه خبر"] = Intent.Wellbeing,
        ["چه خبرا"] = Intent.Wellbeing,
        ["چخبر"] = Intent.Wellbeing,
        ["خسته نباشی"] = Intent.Wellbeing,
        ["خسته نباشید"] = Intent.Wellbeing,
        ["ممنون"] = Intent.Thanks,
        ["مرسی"] = Intent.Thanks,
        ["متشکرم"] = Intent.Thanks,
        ["خیلی ممنون"] = Intent.Thanks,
        ["ممنونم"] = Intent.Thanks,
        ["سپاس"] = Intent.Thanks,
        ["دمت گرم"] = Intent.Thanks,
        ["دستت درد نکنه"] = Intent.Thanks,
        ["thanks"] = Intent.Thanks,
        ["خداحافظ"] = Intent.Farewell,
        ["خدا حافظ"] = Intent.Farewell,
        ["فعلا"] = Intent.Farewell,
        ["شب بخیر"] = Intent.Farewell,
        ["بای"] = Intent.Farewell,
        ["bye"] = Intent.Farewell,
        ["تو کی هستی"] = Intent.About,
        ["کی هستی"] = Intent.About,
        ["خودت رو معرفی کن"] = Intent.About,
        ["چه کاری انجام میدی"] = Intent.About,
        ["چه کار میکنی"] = Intent.About,
        ["کجا زندگی میکنی"] = Intent.Location,
        ["کجا زندگی می کنی"] = Intent.Location,
        ["کجا هستی"] = Intent.Location,
        ["اهل کجایی"] = Intent.Location,
        ["امروز میخوای چی کنی"] = Intent.DailyPlans,
        ["امروز میخوای چی کار کنی"] = Intent.DailyPlans,
        ["امروز می خوای چی کار کنی"] = Intent.DailyPlans,
        ["امروز چه برنامه ای داری"] = Intent.DailyPlans,
        ["عزیزم"] = Intent.None,
        ["رفیق"] = Intent.None,
        ["دوست عزیز"] = Intent.None,
        ["دوست من"] = Intent.None,
        ["داداش"] = Intent.None,
        ["سلامت باشی"] = Intent.Thanks
    }.ToFrozenDictionary();

    [GeneratedRegex(@"(\p{L})\1{2,}")]
    private static partial Regex RepeatedLetters();

    public static string? GetReply(string question) => GetTopic(question) is { } topic ? ReplyForTopic(topic) : null;

    public static ConversationTopic? GetTopic(string question)
    {
        var words = RepeatedLetters().Replace(PersianText.Normalize(question), "$1").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 24)
        {
            return null;
        }

        var intent = Intent.None;
        for (var offset = 0; offset < words.Length;)
        {
            var consumed = 0;
            for (var count = Math.Min(8, words.Length - offset); count > 0; count--)
            {
                if (!Phrases.TryGetValue(string.Join(' ', words, offset, count), out var found))
                {
                    continue;
                }

                if ((int)found > (int)intent)
                {
                    intent = found;
                }

                consumed = count;
                break;
            }

            if (consumed == 0)
            {
                return null;
            }

            offset += consumed;
        }

        return intent switch
        {
            Intent.Greeting => ConversationTopic.Greeting,
            Intent.Wellbeing => ConversationTopic.Wellbeing,
            Intent.Thanks => ConversationTopic.Thanks,
            Intent.Farewell => ConversationTopic.Farewell,
            Intent.About => ConversationTopic.Capabilities,
            Intent.Location => ConversationTopic.Location,
            Intent.DailyPlans => ConversationTopic.DailyPlans,
            _ => null
        };
    }

    public static string ReplyForTopic(ConversationTopic topic) => topic switch
    {
        ConversationTopic.Greeting => "سلام! خوش آمدید 🌿 چطور می‌توانم کمکتان کنم؟",
        ConversationTopic.Wellbeing => "ممنون که پرسیدید! آماده‌ام با شما گپ بزنم و کمکتان کنم. شما چطورید؟",
        ConversationTopic.Identity => "من SkinRag، یک دستیار هوش مصنوعی هستم. سن، بدن یا زندگی شخصی ندارم؛ می‌توانم با شما گپ بزنم و در پیدا کردن محصولات مراقبتی کمک کنم.",
        ConversationTopic.Location => "من یک دستیار هوش مصنوعی‌ام و محل زندگی یا خانه‌ای ندارم؛ از همین گفت‌وگو با شما در ارتباطم.",
        ConversationTopic.DailyPlans => "برنامه روزانه شخصی ندارم؛ اینجا هستم تا با شما گفت‌وگو کنم و کمکتان کنم. شما برای امروز چه برنامه‌ای دارید؟",
        ConversationTopic.Capabilities => "می‌توانم با شما گپ بزنم و محصولات پوست، مو و زیبایی را بر اساس نیازتان بررسی کنم. قیمت، موجودی و مشخصات محصول را از کاتالوگ می‌خوانم.",
        ConversationTopic.Thanks => "خواهش می‌کنم! خوشحال می‌شوم اگر باز هم کمکی از دستم بربیاید.",
        ConversationTopic.Farewell => "خدانگهدار! هر وقت خواستید، گفت‌وگو را ادامه می‌دهیم 🌿",
        _ => "می‌توانیم کمی گپ بزنیم. دوست دارید درباره چه موضوعی صحبت کنیم؟"
    };

    public static string ClarificationFor(ClarificationKind? kind) => kind switch
    {
        ClarificationKind.ProductReference => "منظورتان کدام محصول است؟ نام یا شناسه آن را می‌فرمایید؟",
        ClarificationKind.ProductType => "دنبال چه نوع محصولی هستید؛ مثلاً شوینده، مرطوب‌کننده، شامپو یا یک محصول آرایشی؟",
        ClarificationKind.Preferences => "برای انتخاب دقیق‌تر، نیاز اصلی و نوع پوست یا مویتان را می‌فرمایید؟ بودجه هم اگر مدنظرتان است بگویید.",
        ClarificationKind.BudgetCurrency => "مبلغ بودجه را با واحد ریال یا تومان می‌فرمایید؟",
        ClarificationKind.BudgetAmount => "سقف بودجه موردنظرتان کدام مبلغ است؟ لطفاً مبلغ و واحد را مشخص کنید.",
        _ => "کمی بیشتر توضیح می‌دهید منظورتان چیست؟ می‌خواهید گپ بزنیم یا درباره محصولی کمک می‌خواهید؟"
    };
}
