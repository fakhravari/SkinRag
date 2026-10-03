using System.Text.RegularExpressions;
using System.Globalization;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Models;
using SkinRag.Api.Prompts;

namespace SkinRag.Api.Application.Retrieval;

public sealed partial class QueryBuilder(IOllamaClient ollama, IConfiguration configuration, ILogger<QueryBuilder> logger) : IQueryBuilder
{
    [GeneratedRegex(@"(?:#|\[|شناسه\s*)([0-9]{1,10})(?:\]|\b)")]
    private static partial Regex ProductReferences();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>milliliters?|ml|میلی\s*لیتر|میلیلیتر|میل|grams?|g|گرم|pcs|عدد|تایی)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitSize();

    public async Task<SearchPlan> BuildAsync(
        ConsultationRequest request,
        string message,
        IntentDecision decision,
        ConversationState conversation,
        CatalogVocabulary vocabulary,
        CancellationToken ct)
    {
        var ids = ReferencedIds(message);
        var budgetInput = BudgetParser.Parse(message);
        var writtenBudget = budgetInput.MaximumPriceRials;
        if (request.MaxPrice is null && budgetInput.NeedsClarification)
        {
            return new(
                message,
                CopyFilters(request),
                decision.Intent,
                [],
                false,
                [],
                NeedsMoreInformation: true,
                FollowUpQuestion: budgetInput.FollowUpQuestion,
                Source: "ambiguous-budget");
        }

        var text = PersianText.Normalize(message);
        var isPersianDrySkinMoisturizer = decision.Intent == ConsultationIntent.ProductSearch
            && IsDrySkinMoisturizerRequest(text);
        var ruleDomain = request.Domain ?? ResolveDomain(text, vocabulary.Categories);
        var ruleAttributeText = PersianText.Normalize($"{text} {request.Concern}");
        var candidateCategorySlug = decision.Intent == ConsultationIntent.ProductSearch
            ? ExplicitRuleCategorySlug(text, ruleDomain) ?? MatchUniqueCategory(ruleAttributeText, vocabulary.Categories, ruleDomain)?.Slug
            : null;
        var ruleCategorySlug = candidateCategorySlug is not null
            && (ruleDomain is null || CategoryBelongsToDomain(candidateCategorySlug, ruleDomain, vocabulary.Categories))
                ? candidateCategorySlug
                : null;
        var isContextual = decision.Intent == ConsultationIntent.FollowUp
            || HasAny(
            text,
            "این",
            "اون",
            "همین",
            "اولی",
            "دومی",
            "قیمتش",
            "ترکیباتش",
            "موجوده",
            "ارزان ترش",
            "ارزان",
            "ارزون",
            "اقتصادی",
            "ارزون ترش");
        if (ids.Length == 0 && isContextual && conversation.ProductIds.Length > 0
            && decision.Intent is ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison or ConsultationIntent.PriceInquiry or ConsultationIntent.AvailabilityInquiry)
        {
            ids = HasAny(text, "اولی") ? conversation.ProductIds.Take(1).ToArray() : HasAny(text, "دومی") ? conversation.ProductIds.Skip(1).Take(1).ToArray() : conversation.ProductIds;
        }

        if (isContextual && ids.Length == 0 && conversation.UserQuestions.Length == 0
            && !request.History.Any(x => x.Role == "user")
            && !HasAny(text, "پوست", "مو", "شامپو", "کرم", "ضدآفتاب", "رژ", "ریمل"))
        {
            return new(
                message,
                CopyFilters(request),
                decision.Intent,
                [],
                false,
                [],
                NeedsMoreInformation: true,
                FollowUpQuestion: "نام یا شناسه محصول موردنظر را می‌فرمایید؟",
                Source: "missing-context");
        }

        if (ids.Length > 0)
        {
            var explicitFilters = CopyFilters(request);
            ApplyBudget(explicitFilters, budgetInput, conversation.PendingBudgetRials, conversation.PendingMinimumBudgetRials);
            return CheckBudgetBounds(new(
                message,
                explicitFilters,
                decision.Intent,
                request.ConcernSlug is null ? [] : [request.ConcernSlug],
                false,
                ids,
                InStockOnly: decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
                Source: "product-reference"));
        }

        var previous = isContextual ? conversation.SearchQuery ?? conversation.UserQuestions.LastOrDefault() ?? request.History.LastOrDefault(x => x.Role == "user")?.Content : null;
        if (request.FiltersOnly)
        {
            var terms = new List<string>();
            if (request.Domain is { } selectedDomain)
                terms.Add(vocabulary.Categories.FirstOrDefault(x => x.ParentId is null && x.Domain == selectedDomain)?.Name ?? selectedDomain);
            if (request.CategorySlug is { } categorySlug && vocabulary.Categories.FirstOrDefault(x => x.Slug == categorySlug) is { } category)
                terms.Add(category.Name);
            if (request.SkinType is { } skinType && vocabulary.Profiles.FirstOrDefault(x => x.Slug == skinType) is { } skinProfile)
                terms.Add(skinProfile.Name);
            if (request.HairType is { } hairType && vocabulary.Profiles.FirstOrDefault(x => x.Slug == hairType) is { } hairProfile)
                terms.Add(hairProfile.Name);
            if (request.BrandSlug is { } brandSlug && vocabulary.Brands.FirstOrDefault(x => x.Slug == brandSlug) is { } brand)
                terms.Add(brand.Name);
            if (request.ConcernSlug is { } concernSlug && vocabulary.Concerns.FirstOrDefault(x => x.Slug == concernSlug) is { } concern)
                terms.Add(concern.Name);
            if (terms.Count == 0)
                terms.Add("محصولات مراقبتی");

            var filterCopy = CopyFilters(request);
            ApplyBudget(filterCopy, budgetInput, conversation.PendingBudgetRials, conversation.PendingMinimumBudgetRials);
            return CheckBudgetBounds(new(
                string.Join(" ", terms),
                filterCopy,
                ConsultationIntent.ProductSearch,
                request.ConcernSlug is null ? [] : [request.ConcernSlug],
                request.MaxPrice.HasValue,
                [],
                Source: "filters-only"));
        }

        QueryModelOutput parsed;
        var source = "model";
        var explicitSize = ParseExplicitSize(text);
        var ruleOutput = CreateRuleOutput(text, ruleAttributeText, ruleDomain, ruleCategorySlug, vocabulary);
        var hasRuleFilters = HasRuleFilters(ruleOutput, explicitSize)
            || HasExplicitRequestFilters(request)
            || writtenBudget.HasValue
            || budgetInput.MinimumPriceRials.HasValue;
        if (isPersianDrySkinMoisturizer)
        {
            source = "persian-product-rule";
            parsed = CreateRuleOutput(text, ruleAttributeText, "skin", "face-moisturizer", vocabulary);
        }
        else if (decision.Intent == ConsultationIntent.ProductSearch && hasRuleFilters)
        {
            source = ruleCategorySlug is null ? "explicit-filter-rules" : "explicit-category-rule";
            parsed = ruleOutput;
        }
        else
        {
        try
        {
            parsed = await ExtractModelQueryAsync(request, message, previous, decision, vocabulary, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is (OperationCanceledException or HttpRequestException or InvalidModelOutputException))
        {
            logger.LogWarning(
                "Query extraction unavailable ({ErrorType}); using explicit filters and customer wording",
                ex.GetType().Name);
            source = $"explicit-filters:{ex.GetType().Name}";
            var fallbackText = previous is null ? message : previous + " " + message;
            var fallbackSearchText = $"{fallbackText} {CustomerLanguageQuery.ExpandTerms(fallbackText)}";
            var domain = request.Domain ?? ResolveDomain(fallbackSearchText, vocabulary.Categories);
            var profileMatches = vocabulary.Profiles.Where(x => (domain is null || x.Kind == domain) && LabelScore(PersianText.Normalize(fallbackSearchText), x.Name) > 0)
                .ToArray();
            parsed = new()
            {
                Query = fallbackSearchText[..Math.Min(500, fallbackSearchText.Length)],
                Domain = domain,
                SkinType = profileMatches.Length == 1 && profileMatches[0].Kind == "skin" ? profileMatches[0].Slug : null,
                HairType = profileMatches.Length == 1 && profileMatches[0].Kind == "hair" ? profileMatches[0].Slug : null,
                CategorySlug = ExplicitCategory(PersianText.Normalize(fallbackSearchText), domain, vocabulary)
            };
        }
        }

        if (ruleDomain is not null)
        {
            parsed = ConstrainToDomain(parsed, ruleDomain, ruleCategorySlug, vocabulary.Categories);
        }

        var filters = MergeFilters(request, parsed, decision, conversation, vocabulary, writtenBudget);
        if (filters.SizeValue is null && explicitSize is not null)
        {
            filters.SizeValue = explicitSize.Value;
            filters.SizeUnit = explicitSize.Unit;
        }
        if (budgetInput.MinimumPriceRials is { } writtenMinimum && request.MinPrice is null)
            filters.MinPrice = writtenMinimum;
        else
            filters.MinPrice ??= conversation.PendingMinimumBudgetRials;

        var concerns = request.ConcernSlug is not null ? new[] {
            request.ConcernSlug
        } : parsed.ConcernSlugs;
        concerns = concerns.Where(s => vocabulary.Concerns.Any(c => c.Slug == s && (filters.Domain is null || c.Domain == filters.Domain)))
            .Distinct()
            .ToArray();
        // Matte/glow is already an exact variant-level finish filter. Requiring the
        // corresponding product-level concern as well incorrectly excludes variants
        // whose products are tagged with lip-color but not matte-look/glow-look.
        if (request.ConcernSlug is null && filters.Domain == "beauty" && filters.Finish is not null)
        {
            concerns = concerns.Where(s => s is not ("matte-look" or "glow-look")).ToArray();
        }

        // Relative requests seek alternatives, rather than being pinned to the earlier product IDs.
        var budget = parsed.PricePreference == "budget" || IsBudgetRequest(text);
        return CheckBudgetBounds(new(
            CustomerLanguageQuery.AppendCatalogTerms(parsed.Query, message),
            filters,
            decision.Intent,
            concerns,
            budget,
            ids,
            InStockOnly: decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
            Source: source));
    }

    public static bool IsValid(QueryModelOutput value, CatalogVocabulary vocabulary)
    {
        if (string.IsNullOrWhiteSpace(value.Query) || value.Query.Length > 500
            || value.PricePreference is not ("budget" or "neutral")
            || value.ConcernSlugs is null
            || value.ConcernSlugs.Length > 3
            || value.ExcludeIngredientSlugs is null
            || value.ExcludeIngredientSlugs.Length > 20
            || value.Domain is not null && !vocabulary.Categories.Any(x => x.Domain == value.Domain))
        {
            return false;
        }

        if (value.SkinType is not null
            && !vocabulary.Profiles.Any(x => x.Kind == "skin" && x.Slug == value.SkinType))
        {
            return false;
        }

        if (value.HairType is not null
            && !vocabulary.Profiles.Any(x => x.Kind == "hair" && x.Slug == value.HairType))
        {
            return false;
        }

        if (value.CategorySlug is not null
            && !vocabulary.Categories.Any(x => x.Slug == value.CategorySlug
                && (value.Domain is null || CategoryBelongsToDomain(x.Slug, value.Domain, vocabulary.Categories))))
        {
            return false;
        }

        if (value.BrandSlug is not null && !vocabulary.Brands.Any(x => x.Slug == value.BrandSlug))
        {
            return false;
        }

        if (value.Shade is not null && !(vocabulary.Shades ?? []).Contains(value.Shade, StringComparer.Ordinal))
        {
            return false;
        }

        if (value.Finish is not null && !(vocabulary.Finishes ?? []).Contains(value.Finish, StringComparer.Ordinal))
        {
            return false;
        }

        return value.ConcernSlugs.All(s => vocabulary.Concerns.Any(x => x.Slug == s))
            && value.ExcludeIngredientSlugs.All(s => vocabulary.Ingredients.Any(x => x.Slug == s));
    }

    public static int[] ReferencedIds(string message) => ProductReferences().Matches(message).Select(x => int.TryParse(x.Groups[1].Value, out var id) ? id : 0)
        .Where(x => x > 0)
        .Distinct()
        .Take(10)
        .ToArray();

    public static decimal? ParseMaximumPrice(string text) => BudgetParser.Parse(text).MaximumPriceRials;

    public static CatalogFilters CopyFilters(CatalogFilters f) => new()
    {
        Domain = f.Domain,
        CategorySlug = f.CategorySlug,
        BrandSlug = f.BrandSlug,
        SkinType = f.SkinType,
        HairType = f.HairType,
        ConcernSlug = f.ConcernSlug,
        MinPrice = f.MinPrice,
        MaxPrice = f.MaxPrice,
        Shade = f.Shade,
        Finish = f.Finish,
        SizeValue = f.SizeValue,
        SizeUnit = f.SizeUnit,
        FragranceFree = f.FragranceFree,
        ExcludeIngredientSlugs = f.ExcludeIngredientSlugs.ToArray()
    };

    private static void ApplyBudget(CatalogFilters filters, ParsedBudget writtenBudget, decimal? pendingBudget, decimal? pendingMinimum)
    {
        filters.MinPrice ??= writtenBudget.MinimumPriceRials ?? pendingMinimum;
        filters.MaxPrice ??= writtenBudget.MaximumPriceRials ?? pendingBudget;
    }

    private static SearchPlan CheckBudgetBounds(SearchPlan plan) => plan.Filters.MinPrice > plan.Filters.MaxPrice
        ? plan with
        {
            NeedsMoreInformation = true,
            FollowUpQuestion = "حداقل قیمت از سقف بودجه بیشتر است؛ حداقل قیمت یا سقف بودجه را تغییر می‌دهید؟",
            Source = "conflicting-price-bounds"
        }
        : plan;
    private static bool HasAny(string text, params string[] values) => values.Any(s => (" " + text + " ").Contains(" " + s + " ", StringComparison.Ordinal));

    private static int LabelScore(string text, string label)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] suffixes = ["", "ه", "م", "ها", "های", "ی", "تر", "تری"];
        return PersianText.SearchTokens(label).Where(t => t.Length >= 2
            && t is not ("پوست" or "صورت" or "مو" or "موی" or "مراقبت" or "کننده" or "انواع" or "همه"))
            .Count(t => words.Any(word => suffixes.Any(s => word == t + s)));
    }

    private static string? ScopeDomain(string message)
    {
        var text = PersianText.Normalize(message);
        var fragrance = HasAny(text,
                "خوشبو کننده", "خوشبوکننده", "خوشبو کننده بدن", "عطر", "ادکلن", "بادی اسپلش", "ضد تعریق", "ضدتعریق")
            && !HasAny(text, "بدون عطر", "بدون رایحه", "فاقد عطر", "فاقد رایحه", "بی عطر", "عاری از عطر");
        var cellulose = HasAny(text, "سلولزی", "دستمال", "گوش پاک کن", "گوش پاککن", "پد آرایش پاک کن", "نوار بهداشتی", "پد روزانه");
        var bundles = HasAny(text, "بسته ترکیبی", "بسته های ترکیبی", "بسته‌های ترکیبی", "پک ترکیبی", "بسته محصولات");
        var campaigns = HasAny(text, "جشنواره شگفت انگیز", "جشنواره شگفت‌انگیز", "جشنواره");
        var food = HasAny(text, "نوشیدنی", "خوراکی", "نوشیدنی سرد", "نوشیدنی گرم");
        var promotional = HasAny(text,
            "کالای تبلیغاتی", "محصول تبلیغاتی", "محتوای آموزشی", "محتوی آموزشی", "نشان و جوایز", "تجهیزات جانبی",
            "پوشاک", "لباس", "جوراب", "شال", "پیراهن", "تی شرت", "کتاب", "مجله", "بروشور", "کاتالوگ", "دفترچه فاکتور", "کلربوک");
        var other = HasAny(text, "محصولات دیگر", "محصول دیگر", "سایر محصولات");
        var personalCare = HasAny(text,
            "بهداشتی و مراقبتی", "مراقبت دست و صورت", "مراقبت دست و ناخن", "مراقبت دهان و دندان", "مراقبت پا",
            "مسواک", "خمیر دندان", "نخ دندان", "ضد ترک پا", "مراقبت بدن", "شامپو بدن");
        var hair = HasAny(text, "مو", "موی", "موهام", "موها", "شامپو", "نرم کننده", "کراتین", "روغن مو", "سرم مو", "ماسک مو", "موس مو");
        var beauty = HasAny(
            text,
            "آرایش", "آرایشی", "زیبایی", "رژ", "ریمل", "کانسیلر", "سایه", "خط چشم", "کرم پودر", "لاک", "رژگونه", "برنزر", "برانزر",
            "برق لب", "لیپ گلاس", "بی بی کرم", "پرایمر", "پنکیک", "مداد ابرو", "هایلایتر", "آی لاینر", "فاندیشن");
        var skin = HasAny(
            text,
            "پوست",
            "پوستم",
            "ضدآفتاب",
            "ضد آفتاب",
            "آبرسان",
            "مرطوب کننده",
            "شوینده",
            "صورت",
            "شامپو بدن",
            "بدن",
            "دست",
            "بالم لب",
            "میسلار",
            "لوسیون");
        if (text.Contains("شامپو بدن"))
        {
            hair = false;
        }

        // A specific catalog class takes precedence over a generic word such as
        // "آرایش" inside "پد آرایش پاک‌کن" or "بهداشتی" inside "نوار بهداشتی".
        if (cellulose || food || bundles || campaigns || other || personalCare || promotional)
        {
            beauty = false;
            skin = false;
            hair = false;
        }

        return (fragrance, cellulose, food, bundles, campaigns, other, personalCare, promotional, hair, beauty, skin) switch
        {
            (true, false, false, false, false, false, false, false, false, false, false) => "fragrance",
            (false, true, false, false, false, false, false, false, false, false, false) => "cellulose",
            (false, false, true, false, false, false, false, false, false, false, false) => "food",
            (false, false, false, true, false, false, false, false, false, false, false) => "bundles",
            (false, false, false, false, true, false, false, false, false, false, false) => "campaigns",
            (false, false, false, false, false, true, false, false, false, false, false) => "other",
            (false, false, false, false, false, false, true, false, false, false, false) => "personal-care",
            (false, false, false, false, false, false, false, true, false, false, false) => "promotional",
            (false, false, false, false, false, false, false, false, true, false, false) => "hair",
            (false, false, false, false, false, false, false, false, false, true, false) => "beauty",
            (false, false, false, false, false, false, false, false, false, false, true) => "skin",
            _ => null
        };
    }

    private static string? ExplicitCategory(string text, string? domain, CatalogVocabulary vocabulary)
    {
        var slug = ExplicitRuleCategorySlug(text, domain);
        if (vocabulary.Categories.Any(x => x.Slug == slug))
        {
            return slug;
        }

        return vocabulary.Categories
            .Select(x => new { x.Slug, Score = LabelScore(text, x.Name) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Slug)
            .FirstOrDefault();
    }

    private static bool IsDrySkinMoisturizerRequest(string text)
    {
        var drySkin = HasAny(text, "خشک", "خشکه", "خشکی")
            && HasAny(text, "پوست", "پوستم", "پوستت", "پوستام", "پوستای", "پوستها", "پوست های", "پوستهای", "صورت", "صورتم");
        var moisturizer = HasAny(text, "مرطوب کننده", "مرطوبکننده", "آبرسان");
        return drySkin && moisturizer;
    }

    private static bool IsBudgetRequest(string text) => HasAny(text,
        "ارزان", "ارزانتر", "ارزان تر", "ارزان قیمت", "قیمت ارزان", "ارزون", "ارزونتر", "ارزون تر",
        "اقتصادی", "به صرفه", "مقرون به صرفه", "قیمت مناسب");

    private static string? ExplicitRuleCategorySlug(string text, string? domain) => domain switch
        {
            "skin" when HasAny(text, "ضدآفتاب", "ضد آفتاب") => "sun-screen",
            "skin" when HasAny(text, "مرطوب کننده", "مرطوبکننده", "آبرسان", "آبرسانی", "moisturizer", "moisturiser") => "face-moisturizer",
            "skin" when HasAny(text, "شوینده") => "gentle-cleanser",
            "hair" when HasAny(text, "کرم")
                && HasAny(text, "مو", "موی", "موها", "موهای", "موهام")
                && HasAny(text, "بدون آبکشی", "بدون نیاز به آبکشی", "leave in", "leave-in", "leavein", "بعد حمام", "بعد از حمام", "پس از حمام", "نشورمش", "نشورم") => "leave-in",
            "beauty" when HasAny(text, "رژلب", "lipstick", "lip stick") || domain == "beauty" && HasAny(text, "رژ") && HasAny(text, "لب") => "lipstick",
            "beauty" when HasAny(text, "ریمل") => "mascara",
            _ => null
        };

    private static Profile? MatchUniqueProfile(string text, IEnumerable<Profile> profiles)
    {
        var matches = profiles.Where(x => LabelScore(text, x.Name) > 0).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string[] MatchExplicitConcerns(string text, IEnumerable<Concern> concerns)
    {
        var matches = concerns.Where(x => LabelScore(text, x.Name + " " + x.SearchTerms) > 0)
            .Select(x => x.Slug)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return matches.Length is > 0 and <= 3 ? matches : [];
    }

    private static string? MatchExplicitCatalogValue(string text, string[]? values)
    {
        var matches = (values ?? [])
            .Where(value => HasAny(text, PersianText.Normalize(value)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private sealed record ParsedSize(decimal Value, string Unit);

    private static QueryModelOutput CreateRuleOutput(
        string text,
        string attributeText,
        string? domain,
        string? categorySlug,
        CatalogVocabulary vocabulary)
    {
        var profile = MatchUniqueProfile(attributeText, vocabulary.Profiles.Where(x => domain is null || x.Kind == domain));
        return new()
        {
            Query = text,
            Domain = domain,
            CategorySlug = categorySlug,
            BrandSlug = MatchUniqueBrand(text, vocabulary.Brands)?.Slug,
            SkinType = profile?.Kind == "skin" ? profile.Slug : null,
            HairType = profile?.Kind == "hair" ? profile.Slug : null,
            ConcernSlugs = MatchExplicitConcerns(attributeText, vocabulary.Concerns.Where(x => domain is null || x.Domain == domain)),
            Shade = domain == "beauty" ? MatchExplicitCatalogValue(text, vocabulary.Shades) : null,
            Finish = domain == "beauty" ? MatchExplicitCatalogValue(text, vocabulary.Finishes) : null,
            FragranceFree = ParseFragrancePreference(text),
            ExcludeIngredientSlugs = MatchExcludedIngredients(text, vocabulary.Ingredients),
            PricePreference = IsBudgetRequest(text) ? "budget" : "neutral"
        };
    }

    private static string? ResolveDomain(string message, IEnumerable<Category> categories)
    {
        var text = PersianText.Normalize(message);
        var all = categories.ToArray();
        var heuristicDomain = ScopeDomain(text);
        var exact = all.Where(x => HasAny(text, PersianText.Normalize(x.Name))
                || HasAny(text, PersianText.Normalize(x.Slug.Replace('-', ' '))))
            .DistinctBy(x => x.Slug)
            .ToArray();
        if (exact.Length > 0)
        {
            var maximumSpecificity = exact.Max(x => PersianText.SearchTokens(x.Name).Count());
            var mostSpecific = exact.Where(x => PersianText.SearchTokens(x.Name).Count() == maximumSpecificity).ToArray();
            if (mostSpecific.Length == 1
                && (heuristicDomain is null
                    || mostSpecific[0].Domain == heuristicDomain
                    || maximumSpecificity >= 2))
            {
                return mostSpecific[0].Domain;
            }
        }

        return heuristicDomain ?? MatchUniqueCategory(text, all, null)?.Domain;
    }

    private static bool HasRuleFilters(QueryModelOutput parsed, ParsedSize? size) =>
        parsed.Domain is not null
        || parsed.CategorySlug is not null
        || parsed.BrandSlug is not null
        || parsed.SkinType is not null
        || parsed.HairType is not null
        || parsed.ConcernSlugs.Length > 0
        || parsed.Shade is not null
        || parsed.Finish is not null
        || parsed.FragranceFree.HasValue
        || parsed.ExcludeIngredientSlugs.Length > 0
        || parsed.PricePreference == "budget"
        || size is not null;

    private static bool HasExplicitRequestFilters(ConsultationRequest request) =>
        request.Domain is not null
        || request.CategorySlug is not null
        || request.BrandSlug is not null
        || request.SkinType is not null
        || request.HairType is not null
        || request.ConcernSlug is not null
        || request.MinPrice.HasValue
        || request.MaxPrice.HasValue
        || request.Shade is not null
        || request.Finish is not null
        || request.SizeValue.HasValue
        || request.SizeUnit is not null
        || request.FragranceFree.HasValue
        || request.ExcludeIngredientSlugs.Length > 0;

    private static Category? MatchUniqueCategory(string text, IEnumerable<Category> categories, string? domain)
    {
        var all = categories.ToArray();
        var candidates = all.Where(x => domain is null || CategoryBelongsToDomain(x.Slug, domain, all)).ToArray();
        var exact = candidates.Where(x => HasAny(text, PersianText.Normalize(x.Name))
                || HasAny(text, PersianText.Normalize(x.Slug.Replace('-', ' '))))
            .DistinctBy(x => x.Slug)
            .ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        if (exact.Length > 1)
        {
            return null;
        }

        var scored = candidates.Select(x => new { Category = x, Score = LabelScore(text, x.Name) })
            .Where(x => x.Score > 0)
            .ToArray();
        if (scored.Length == 0)
        {
            return null;
        }

        var bestScore = scored.Max(x => x.Score);
        var best = scored.Where(x => x.Score == bestScore).ToArray();
        return best.Length == 1 ? best[0].Category : null;
    }

    private static bool CategoryBelongsToDomain(string slug, string domain, IEnumerable<Category> categories)
    {
        var bySlug = categories.ToDictionary(x => x.Slug, StringComparer.Ordinal);
        if (!bySlug.TryGetValue(slug, out var current))
        {
            return false;
        }

        var byId = bySlug.Values.ToDictionary(x => x.Id);
        var visited = new HashSet<int>();
        while (current is not null && visited.Add(current.Id))
        {
            if (current.Domain == domain)
            {
                return true;
            }

            current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
        }

        return false;
    }

    private static QueryModelOutput ConstrainToDomain(
        QueryModelOutput parsed,
        string domain,
        string? matchedCategorySlug,
        IEnumerable<Category> categories)
    {
        var categorySlug = matchedCategorySlug
            ?? (parsed.CategorySlug is not null && CategoryBelongsToDomain(parsed.CategorySlug, domain, categories)
                ? parsed.CategorySlug
                : null);
        return new()
        {
            Query = parsed.Query,
            Domain = domain,
            CategorySlug = categorySlug,
            BrandSlug = parsed.BrandSlug,
            SkinType = domain == "skin" ? parsed.SkinType : null,
            HairType = domain == "hair" ? parsed.HairType : null,
            Shade = domain == "beauty" ? parsed.Shade : null,
            Finish = domain == "beauty" ? parsed.Finish : null,
            ConcernSlugs = parsed.ConcernSlugs,
            ExcludeIngredientSlugs = parsed.ExcludeIngredientSlugs,
            FragranceFree = parsed.FragranceFree,
            PricePreference = parsed.PricePreference
        };
    }

    private static Brand? MatchUniqueBrand(string text, IEnumerable<Brand> brands)
    {
        var matches = brands.Where(x => HasAny(text, PersianText.Normalize(x.Name))
                || HasAny(text, PersianText.Normalize(x.Slug.Replace('-', ' '))))
            .DistinctBy(x => x.Slug)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool? ParseFragrancePreference(string text)
    {
        var fragranceFree = HasAny(text,
            "بدون عطر", "بدون رایحه", "فاقد عطر", "فاقد رایحه", "عاری از عطر", "بی عطر", "fragrance free", "unscented");
        var scented = HasAny(text, "معطر", "عطر دار", "رایحه دار", "scented");
        return fragranceFree == scented ? null : fragranceFree;
    }

    private static string[] MatchExcludedIngredients(string text, IEnumerable<Ingredient> ingredients) => ingredients
        .Where(x => HasNegatedIngredientMention(text, x.Name)
            || HasNegatedIngredientMention(text, x.InciName)
            || HasNegatedIngredientMention(text, x.Slug.Replace('-', ' ')))
        .Select(x => x.Slug)
        .Distinct(StringComparer.Ordinal)
        .Take(20)
        .ToArray();

    private static bool HasNegatedIngredientMention(string text, string term)
    {
        var normalizedTerm = PersianText.Normalize(term);
        if (normalizedTerm.Length == 0)
        {
            return false;
        }

        var padded = $" {text} ";
        var needle = $" {normalizedTerm} ";
        var offset = 0;
        while ((offset = padded.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            var before = padded[Math.Max(0, offset - 70)..offset];
            var afterStart = offset + needle.Length;
            var after = padded[afterStart..Math.Min(padded.Length, afterStart + 70)];
            if (HasAny(before, "بدون", "فاقد", "عاری از", "نمیخوام", "نمی خواهم", "نمیخواهم", "free from", "without")
                || HasAny(after, "نباشد", "نباشه", "نداشته باشد", "نداشته باشه", "نمیخوام", "نمی خواهم", "نمیخواهم", "do not want"))
            {
                return true;
            }

            offset += needle.Length;
        }

        return false;
    }

    private static ParsedSize? ParseExplicitSize(string text)
    {
        var normalized = PersianText.Normalize(InputNormalizer.Normalize(text));
        var matches = ExplicitSize().Matches(normalized)
            .Select(match =>
            {
                var rawValue = match.Groups["value"].Value.Replace(',', '.');
                if (!decimal.TryParse(rawValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                {
                    return null;
                }

                var unit = PersianText.Normalize(match.Groups["unit"].Value);
                var normalizedUnit = unit is "g" or "gram" or "grams" or "گرم" ? "g"
                    : unit is "pcs" or "عدد" or "تایی" ? "pcs"
                    : "ml";
                return value is >= 0.01m and <= 100_000m ? new ParsedSize(value, normalizedUnit) : null;
            })
            .Where(x => x is not null)
            .DistinctBy(x => (x!.Value, x.Unit))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task<QueryModelOutput> ExtractModelQueryAsync(
        ConsultationRequest request,
        string message,
        string? previous,
        IntentDecision decision,
        CatalogVocabulary vocabulary,
        CancellationToken ct)
    {
        // Keep extraction context small enough for the configured local model window.
        // This narrows the vocabulary only; an inferred scope is not a hard SQL filter.
        var customerLanguageTerms = CustomerLanguageQuery.ExpandTerms(message);
        var scopeText = $"{message} {previous} {customerLanguageTerms}";
        var scope = request.Domain ?? ResolveDomain(scopeText, vocabulary.Categories);
        var searchText = PersianText.Normalize(scopeText);
        var scoped = vocabulary with
        {
            Categories = vocabulary.Categories.Where(x => scope is null || CategoryBelongsToDomain(x.Slug, scope, vocabulary.Categories)).Select(x => new
            {
                Value = x,
                Score = LabelScore(searchText, x.Name)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .Select(x => x.Value)
            .ToArray(),
            Concerns = vocabulary.Concerns.Where(x => scope is null || x.Domain == scope).Where(x => LabelScore(searchText, x.Name + " " + x.SearchTerms) > 0)
            .Take(3)
            .ToArray(),
            Profiles = vocabulary.Profiles.Where(x => (scope is null || x.Kind == scope) && LabelScore(searchText, x.Name) > 0)
            .Take(4)
            .ToArray(),
            Brands = vocabulary.Brands.Where(x => searchText.Contains(PersianText.Normalize(x.Name), StringComparison.Ordinal)
|| (x.Slug.StartsWith("ldora", StringComparison.Ordinal) && searchText.Contains("لدورا")))
            .ToArray(),
            Ingredients = vocabulary.Ingredients.Where(x => request.ExcludeIngredientSlugs.Contains(x.Slug)
|| PersianText.Normalize(message).Contains(PersianText.Normalize(x.Name), StringComparison.Ordinal)
|| message.Contains(x.Slug, StringComparison.OrdinalIgnoreCase))
            .ToArray()
        };
        object SlugArray(IEnumerable<string> values, int maximum) => new
        {
            type = "array",
            maxItems = maximum,
            items = values.Any() ? (object)new
            {
                type = "string",
                @enum = values.ToArray()
            } : new
            {
                type = "string"
            }
        };
        var properties = new Dictionary<string, object>
        {
            ["query"] = new
            {
                type = "string",
                minLength = 1,
                maxLength = 500
            },
            ["domain"] = PipelinePrompts.NullableEnum(vocabulary.Categories.Select(x => x.Domain).Distinct(StringComparer.Ordinal)),
            ["skinType"] = PipelinePrompts.NullableEnum(scoped.Profiles.Where(x => x.Kind == "skin").Select(x => x.Slug)),
            ["hairType"] = PipelinePrompts.NullableEnum(scoped.Profiles.Where(x => x.Kind == "hair").Select(x => x.Slug)),
            ["categorySlug"] = PipelinePrompts.NullableEnum(scoped.Categories.Select(x => x.Slug)),
            ["brandSlug"] = PipelinePrompts.NullableEnum(scoped.Brands.Select(x => x.Slug)),
            ["shade"] = PipelinePrompts.NullableEnum(vocabulary.Shades ?? []),
            ["finish"] = PipelinePrompts.NullableEnum(vocabulary.Finishes ?? []),
            ["concernSlugs"] = SlugArray(scoped.Concerns.Select(x => x.Slug), Math.Min(3, scoped.Concerns.Length)),
            ["excludeIngredientSlugs"] = SlugArray(scoped.Ingredients.Select(x => x.Slug), Math.Min(20, scoped.Ingredients.Length)),
            ["fragranceFree"] = new
            {
                type = new[] {
                    "boolean",
                    "null"
                }
            },
            ["pricePreference"] = new
            {
                type = "string",
                @enum = new[] {
                    "budget",
                    "neutral"
                }
            }
        };
        var schema = PipelinePrompts.Schema(new
        {
            type = "object",
            additionalProperties = false,
            properties,
            required = properties.Keys.ToArray()
        });
        var parsed = await ollama.ChatStructuredAsync<QueryModelOutput>(
            PipelinePrompts.Query,
            new
            {
                message,
                customerLanguageTerms,
                intent = decision.Code,
                previousUserQuestion = previous,
                categories = scoped.Categories.Select(x => new[] { x.Slug, x.Name, x.Domain }),
                brands = scoped.Brands.Select(x => new[] { x.Slug, x.Name }),
                shades = vocabulary.Shades ?? [],
                finishes = vocabulary.Finishes ?? [],
                profiles = scoped.Profiles.Select(x => new[] { x.Slug, x.Name, x.Kind }),
                concerns = scoped.Concerns.Select(x => new[] { x.Slug, x.Name }),
                ingredients = scoped.Ingredients.Select(x => new[] { x.Slug, x.Name })
            },
            schema,
            new(
            "Query",
            configuration.GetValue("Consultation:QueryTimeoutSeconds", 60),
            configuration.GetValue("Consultation:QueryMaxTokens", 220)),
            ct);
        if (!IsValid(parsed, scoped))
        {
            throw new InvalidModelOutputException("Query references unknown or contradictory catalog filters.");
        }

        return parsed;
    }

    private static CatalogFilters MergeFilters(
        ConsultationRequest request,
        QueryModelOutput parsed,
        IntentDecision decision,
        ConversationState conversation,
        CatalogVocabulary vocabulary,
        decimal? writtenBudget)
    {
        var filters = CopyFilters(request);
        filters.Domain ??= parsed.Domain;
        var category = vocabulary.Categories.FirstOrDefault(x => x.Slug == parsed.CategorySlug);
        if (filters.CategorySlug is null && category is not null
            && (filters.Domain is null || CategoryBelongsToDomain(category.Slug, filters.Domain, vocabulary.Categories)))
        {
            filters.CategorySlug = category.Slug;
        }

        filters.SkinType ??= filters.Domain is null or "skin" ? parsed.SkinType : null;
        filters.HairType ??= filters.Domain is null or "hair" ? parsed.HairType : null;
        filters.BrandSlug ??= parsed.BrandSlug;
        filters.Shade ??= parsed.Shade;
        filters.Finish ??= parsed.Finish;
        filters.FragranceFree ??= parsed.FragranceFree;
        filters.MaxPrice ??= writtenBudget ?? conversation.PendingBudgetRials;
        filters.ExcludeIngredientSlugs = filters.ExcludeIngredientSlugs.Concat(parsed.ExcludeIngredientSlugs).Distinct().ToArray();
        if (decision.Intent == ConsultationIntent.FollowUp && conversation.SearchFilters is
            {
            } earlier)
        {
            filters.Domain ??= earlier.Domain;
            filters.CategorySlug ??= earlier.CategorySlug;
            filters.BrandSlug ??= earlier.BrandSlug;
            filters.SkinType ??= earlier.SkinType;
            filters.HairType ??= earlier.HairType;
            filters.MaxPrice ??= earlier.MaxPrice;
            filters.MinPrice ??= earlier.MinPrice;
            filters.Shade ??= earlier.Shade;
            filters.Finish ??= earlier.Finish;
            filters.FragranceFree ??= earlier.FragranceFree;
            filters.ExcludeIngredientSlugs = filters.ExcludeIngredientSlugs.Concat(earlier.ExcludeIngredientSlugs).Distinct().ToArray();
        }

        return filters;
    }
}
