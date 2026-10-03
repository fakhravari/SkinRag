using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Consultation;

public static class GroundedAnswers
{
    public const string ReasonCode = "CATALOG_MATCH";

    // The model chooses wording; all factual text is constructed from current SQL records.
    public static readonly string[] Answers =
    [
        "این گزینه‌ها با نیاز و فیلترهای شما در کاتالوگ فعلی مطابقت دارند.",
        "این محصولات در کاتالوگ فعلی برای بررسی شما انتخاب شدند.",
        "برای چیدن روتین، این گزینه‌های ثبت‌شده را بررسی کنید؛ ترتیب مصرف را از راهنمای هر محصول بگیرید.",
        "برای انتخاب دقیق‌تر، اطلاعات بیشتری لازم است."
    ];

    public static readonly string[] FollowUps =
    [
        "نوع پوست یا موی شما چیست؟", "نام یا شناسه محصول موردنظر را می‌فرمایید؟", "بودجه و نوع محصول موردنظرتان چیست؟"
    ];

    public static readonly Dictionary<string, string> AnswerCodes = new()
    {
        ["MATCHED"] = Answers[0],
        ["CATALOG_OPTIONS"] = Answers[1],
        ["ROUTINE"] = Answers[2],
        ["NEED_MORE"] = Answers[3]
    };

    public static readonly Dictionary<string, string> FollowUpCodes = new()
    {
        ["PROFILE"] = FollowUps[0],
        ["PRODUCT"] = FollowUps[1],
        ["BUDGET"] = FollowUps[2]
    };

    public static string? ResolveAnswer(string? value)
    {
        return value is null ? null : AnswerCodes.GetValueOrDefault(value) ?? (Answers.Contains(value) ? value : null);
    }

    public static string? ResolveFollowUp(string? value)
    {
        return value is null
            ? null
            : FollowUpCodes.GetValueOrDefault(value) ?? (FollowUps.Contains(value) ? value : null);
    }

    public static string Reason(ProductDto p)
    {
        return $"این محصول در دسته «{p.Category ?? "محصولات مراقبتی"}» ثبت شده است.";
    }
}
