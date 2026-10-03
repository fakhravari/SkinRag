using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Domain.Catalog;

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

        var leaveIn = await Build(builder, vocabulary, state,
            "برای موی فر و وز یک کرم مو بدون آبکشی می‌خواهم");
        check(leaveIn.Filters.Domain == "hair" && leaveIn.Filters.CategorySlug == "leave-in"
            && leaveIn.Filters.HairType == "hair-curly"
            && leaveIn.ConcernSlugs.Contains("frizz") && !leaveIn.ConcernSlugs.Contains("curl-style"),
            $"Leave-in request incorrectly routed or constrained: domain={leaveIn.Filters.Domain}, category={leaveIn.Filters.CategorySlug}, hairType={leaveIn.Filters.HairType}, concerns={string.Join(',', leaveIn.ConcernSlugs)}");

        // Exercise the same chat intent with filters supplied by the catalog UI.
        var leaveInCategorySelected = await Build(builder, vocabulary, state,
            "برای موی فر و وز کرم مو می‌خواهم", new ConsultationRequest
            {
                Domain = "hair", CategorySlug = "leave-in", HairType = "hair-curly"
            });
        check(leaveInCategorySelected.Filters.Domain == "hair"
            && leaveInCategorySelected.Filters.CategorySlug == "leave-in"
            && leaveInCategorySelected.Filters.HairType == "hair-curly"
            && leaveInCategorySelected.ConcernSlugs.Contains("frizz")
            && !leaveInCategorySelected.ConcernSlugs.Contains("curl-style"),
            $"UI-selected leave-in category conflicted with chat: {leaveInCategorySelected.Filters.CategorySlug}, {string.Join(',', leaveInCategorySelected.ConcernSlugs)}");

        var leaveInDomainOnly = await Build(builder, vocabulary, state,
            "برای موی فر و وز کرم مو بدون آبکشی می‌خواهم", new ConsultationRequest
            {
                Domain = "hair", HairType = "hair-curly"
            });
        check(leaveInDomainOnly.Filters.Domain == "hair"
            && leaveInDomainOnly.Filters.CategorySlug == "leave-in"
            && !leaveInDomainOnly.ConcernSlugs.Contains("curl-style"),
            "Chat did not refine a domain-only hair selection to leave-in correctly");

        var leaveInFiltersOnly = await Build(builder, vocabulary, state,
            "", new ConsultationRequest
            {
                Question = "", FiltersOnly = true, Domain = "hair", CategorySlug = "leave-in",
                HairType = "hair-curly", ConcernSlug = "frizz"
            });
        check(leaveInFiltersOnly.Filters.Domain == "hair"
            && leaveInFiltersOnly.Filters.CategorySlug == "leave-in"
            && leaveInFiltersOnly.Filters.HairType == "hair-curly"
            && leaveInFiltersOnly.ConcernSlugs.SequenceEqual(["frizz"]),
            "Filters-only leave-in request lost one of the UI-selected filters");

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

        var lipstickCategorySelected = await Build(builder, vocabulary, state,
            "یک رژ لب صورتی با جلوه مات می‌خواهم", new ConsultationRequest
            {
                Domain = "beauty", CategorySlug = "lipstick", Shade = "صورتی", Finish = "مات"
            });
        check(lipstickCategorySelected.Filters.Domain == "beauty"
            && lipstickCategorySelected.Filters.CategorySlug == "lipstick"
            && lipstickCategorySelected.Filters.Shade == "صورتی"
            && lipstickCategorySelected.Filters.Finish == "مات"
            && lipstickCategorySelected.ConcernSlugs.SequenceEqual(["lip-color"]),
            "UI-selected lipstick filters conflicted with the chat request");

        var glow = await Build(builder, vocabulary, state, "یک رژ لب صورتی با جلوه درخشان می‌خواهم");
        check(glow.Filters.Finish == "درخشان" && !glow.ConcernSlugs.Contains("glow-look"),
            "Variant glow finish was also applied as an inferred product-level concern");

        foreach (var (question, expectedCategory) in SpecificCategoryExamples)
        {
            var plan = await Build(builder, vocabulary, state, question);
            check(plan.Filters.CategorySlug == expectedCategory,
                $"A broad keyword overrode the specific category in {question}: expected {expectedCategory}, got {plan.Filters.CategorySlug ?? "none"}");
        }

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

    private static readonly (string Question, string Category)[] SpecificCategoryExamples =
    [
        ("برای سرم آبرسان محصول می‌خواهم", "hydrating-serum"),
        ("برای ژل مرطوب‌کننده محصول می‌خواهم", "light-gel"),
        ("برای شوینده پوست چرب محصول می‌خواهم", "oil-cleanser"),
        ("برای نوشیدنی سرد محصول می‌خواهم", "cold-drink"),
        ("برای گوش پاک کن محصول می‌خواهم", "ear-swab")
    ];

    private static CatalogVocabulary Vocabulary() => new(
    [
        new() { Id = 1, Slug = "face-moisturizer", Name = "مرطوب کننده صورت", Domain = "skin" },
        new() { Id = 13, Slug = "hydrating-serum", Name = "سرم آبرسان", Domain = "skin" },
        new() { Id = 14, Slug = "light-gel", Name = "ژل مرطوب‌کننده", Domain = "skin" },
        new() { Id = 15, Slug = "gentle-cleanser", Name = "شوینده صورت", Domain = "skin" },
        new() { Id = 16, Slug = "oil-cleanser", Name = "شوینده پوست چرب", Domain = "skin" },
        new() { Id = 2, Slug = "curl-cream", Name = "کرم موی فر", Domain = "hair" },
        new() { Id = 3, Slug = "leave-in", Name = "کرم مو بدون آبکشی", Domain = "hair" },
        new() { Id = 4, Slug = "lipstick", Name = "رژ لب", Domain = "beauty" },
        new() { Id = 5, Slug = "toothbrush", Name = "مسواک", Domain = "personal-care" },
        new() { Id = 6, Slug = "perfume", Name = "عطر", Domain = "fragrance" },
        new() { Id = 7, Slug = "wet-wipes", Name = "دستمال مرطوب", Domain = "cellulose" },
        new() { Id = 8, Slug = "promo", Name = "کالای تبلیغاتی", Domain = "promotional" },
        new() { Id = 9, Slug = "cold-drink", Name = "نوشیدنی سرد", Domain = "food", ParentId = 17 },
        new() { Id = 10, Slug = "other", Name = "محصولات دیگر", Domain = "other" },
        new() { Id = 11, Slug = "bundle", Name = "بسته ترکیبی", Domain = "bundles" },
        new() { Id = 12, Slug = "campaign", Name = "جشنواره شگفت انگیزها", Domain = "campaigns" },
        new() { Id = 17, Slug = "drink", Name = "نوشیدنی", Domain = "food" },
        new() { Id = 19, Slug = "ear-swab-parent", Name = "گوش پاک کن", Domain = "cellulose" },
        new() { Id = 20, Slug = "ear-swab", Name = "گوش پاک کن", Domain = "cellulose", ParentId = 19 }
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
