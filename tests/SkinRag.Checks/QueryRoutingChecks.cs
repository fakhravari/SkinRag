using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Models;

internal static class QueryRoutingChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var ai = new ProbeOllama();
        var builder = new QueryBuilder(ai, new ConfigurationBuilder().Build(), NullLogger<QueryBuilder>.Instance);
        using var conversations = new ConversationStore();
        var state = conversations.Read(null);
        var vocabulary = Vocabulary();

        var hair = await Build(builder, vocabulary, state, "برای موی فر و وز، کرم مو می‌خواهم");
        check(hair.Filters.Domain == "hair" && hair.Filters.CategorySlug == "curl-cream"
            && hair.Filters.HairType == "hair-curly"
            && hair.ConcernSlugs.Contains("frizz") && hair.ConcernSlugs.Contains("curl-style"),
            "Curly/frizzy hair cream request was not scoped to its catalog products");

        var lipstick = await Build(builder, vocabulary, state, "یک رژ لب صورتی با جلوه مات می‌خواهم");
        check(lipstick.Filters.Domain == "beauty" && lipstick.Filters.CategorySlug == "lipstick"
            && lipstick.Filters.Shade == "صورتی" && lipstick.Filters.Finish == "مات",
            "Pink matte lipstick request lost its category, shade, or finish");
        check(lipstick.ConcernSlugs.SequenceEqual(["lip-color"]),
            "Variant finish was also applied as an inferred product-level concern");

        var explicitConcern = await Build(builder, vocabulary, state,
            "یک رژ لب صورتی با جلوه مات می‌خواهم", new ConsultationRequest { ConcernSlug = "matte-look" });
        check(explicitConcern.ConcernSlugs.SequenceEqual(["matte-look"]),
            "An explicitly selected concern was removed while handling variant finish");

        var glow = await Build(builder, vocabulary, state, "یک رژ لب صورتی با جلوه درخشان می‌خواهم");
        check(glow.Filters.Finish == "درخشان" && !glow.ConcernSlugs.Contains("glow-look"),
            "Variant glow finish was also applied as an inferred product-level concern");

        foreach (var (question, expectedDomain) in DomainExamples)
        {
            var plan = await Build(builder, vocabulary, state, question);
            check(plan.Filters.Domain == expectedDomain,
                $"Catalog domain routing failed for {question}: expected {expectedDomain}, got {plan.Filters.Domain ?? "none"}");
        }

        check(ai.Stages.Count == 0, "Explicit domain/category examples unexpectedly called a language model");
    }

    private static async Task<SearchPlan> Build(
        QueryBuilder builder,
        CatalogVocabulary vocabulary,
        ConversationState state,
        string question,
        ConsultationRequest? request = null)
    {
        request ??= new ConsultationRequest();
        request.Question = question;
        return await builder.BuildAsync(request, question,
            new IntentDecision(ConsultationIntent.ProductSearch, 1, "query-routing-check"),
            state, vocabulary, default);
    }

    private static readonly (string Question, string Domain)[] DomainExamples =
    [
        ("برای پوست خشک مرطوب کننده می‌خواهم", "skin"),
        ("برای موی فر و وز، کرم مو می‌خواهم", "hair"),
        ("یک رژ لب صورتی با جلوه مات می‌خواهم", "beauty"),
        ("مسواک برای دندان می‌خواهم", "personal-care"),
        ("یک عطر می‌خواهم", "fragrance"),
        ("دستمال مرطوب می‌خواهم", "cellulose"),
        ("کالای تبلیغاتی معرفی کن", "promotional"),
        ("نوشیدنی سرد می‌خواهم", "food"),
        ("محصولات دیگر معرفی کن", "other"),
        ("بسته ترکیبی پیشنهاد بده", "bundles"),
        ("جشنواره شگفت انگیزها را نشان بده", "campaigns")
    ];

    private static CatalogVocabulary Vocabulary() => new(
    [
        new() { Id = 1, Slug = "face-moisturizer", Name = "مرطوب کننده صورت", Domain = "skin" },
        new() { Id = 2, Slug = "curl-cream", Name = "کرم موی فر", Domain = "hair" },
        new() { Id = 3, Slug = "leave-in", Name = "کرم مو بدون آبکشی", Domain = "hair" },
        new() { Id = 4, Slug = "lipstick", Name = "رژ لب", Domain = "beauty" },
        new() { Id = 5, Slug = "toothbrush", Name = "مسواک", Domain = "personal-care" },
        new() { Id = 6, Slug = "perfume", Name = "عطر", Domain = "fragrance" },
        new() { Id = 7, Slug = "wet-wipes", Name = "دستمال مرطوب", Domain = "cellulose" },
        new() { Id = 8, Slug = "promo", Name = "کالای تبلیغاتی", Domain = "promotional" },
        new() { Id = 9, Slug = "cold-drink", Name = "نوشیدنی سرد", Domain = "food" },
        new() { Id = 10, Slug = "other", Name = "محصولات دیگر", Domain = "other" },
        new() { Id = 11, Slug = "bundle", Name = "بسته ترکیبی", Domain = "bundles" },
        new() { Id = 12, Slug = "campaign", Name = "جشنواره شگفت انگیزها", Domain = "campaigns" }
    ],
    [],
    [new() { Slug = "hair-curly", Name = "فر و مجعد", Kind = "hair" }],
    [
        new() { Slug = "frizz", Name = "وز مو", Domain = "hair", SearchTerms = "وز گره مجعد frizz" },
        new() { Slug = "curl-style", Name = "حالت‌دهی موی فر", Domain = "hair", SearchTerms = "فر مجعد curl" },
        new() { Slug = "lip-color", Name = "رنگ لب", Domain = "beauty", SearchTerms = "رژ لب lipstick gloss" },
        new() { Slug = "matte-look", Name = "آرایش مات", Domain = "beauty", SearchTerms = "مات برق matte" },
        new() { Slug = "glow-look", Name = "آرایش درخشان", Domain = "beauty", SearchTerms = "درخشان شاین glow" }
    ],
    [],
    ["صورتی"],
    ["مات", "درخشان"]);
}
