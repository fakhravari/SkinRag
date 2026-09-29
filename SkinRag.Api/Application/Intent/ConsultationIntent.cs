namespace SkinRag.Api.Application.Intent;

public enum ConsultationIntent
{
    Greeting,
    SkinConsultation,
    ProductSearch,
    ProductDetails,
    ProductComparison,
    PriceInquiry,
    AvailabilityInquiry,
    RoutineRecommendation,
    FollowUp,
    OffTopic,
    Unsafe,
    Unclear
}

public sealed record IntentDecision(ConsultationIntent Intent, double Confidence, string Source)
{
    public string Code => IntentCodes.ToCode(Intent);

    public bool IsRelevant => Intent is not (ConsultationIntent.Greeting or ConsultationIntent.OffTopic or ConsultationIntent.Unsafe or ConsultationIntent.Unclear);
}

public static class IntentCodes
{
    private static readonly Dictionary<string, ConsultationIntent> Codes = new(StringComparer.Ordinal)
    {
        ["GREETING"] = ConsultationIntent.Greeting,
        ["SKIN_CONSULTATION"] = ConsultationIntent.SkinConsultation,
        ["PRODUCT_SEARCH"] = ConsultationIntent.ProductSearch,
        ["PRODUCT_DETAILS"] = ConsultationIntent.ProductDetails,
        ["PRODUCT_COMPARISON"] = ConsultationIntent.ProductComparison,
        ["PRICE_INQUIRY"] = ConsultationIntent.PriceInquiry,
        ["AVAILABILITY_INQUIRY"] = ConsultationIntent.AvailabilityInquiry,
        ["ROUTINE_RECOMMENDATION"] = ConsultationIntent.RoutineRecommendation,
        ["FOLLOW_UP"] = ConsultationIntent.FollowUp,
        ["OFF_TOPIC"] = ConsultationIntent.OffTopic,
        ["UNSAFE"] = ConsultationIntent.Unsafe,
        ["UNCLEAR"] = ConsultationIntent.Unclear
    };

    public static string[] All => Codes.Keys.ToArray();

    public static bool TryParse(string code, out ConsultationIntent intent) => Codes.TryGetValue(code, out intent);
    public static string ToCode(ConsultationIntent intent) => Codes.Single(x => x.Value == intent).Key;
}
