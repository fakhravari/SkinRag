using System.Text.RegularExpressions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Models;
using SkinRag.Api.Prompts;

namespace SkinRag.Api.Application.Retrieval;

public sealed partial class QueryBuilder(IOllamaClient ollama, IConfiguration configuration, ILogger<QueryBuilder> logger) : IQueryBuilder
{
    [GeneratedRegex(@"(?:#|\[|شناسه\s*)([0-9]{1,10})(?:\]|\b)")]
    private static partial Regex ProductReferences();

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
            explicitFilters.MaxPrice ??= writtenBudget ?? conversation.PendingBudgetRials;
            return new(
                message,
                explicitFilters,
                decision.Intent,
                request.ConcernSlug is null ? [] : [request.ConcernSlug],
                false,
                ids,
                InStockOnly: decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
                Source: "product-reference");
        }

        var previous = isContextual ? conversation.SearchQuery ?? conversation.UserQuestions.LastOrDefault() ?? request.History.LastOrDefault(x => x.Role == "user")?.Content : null;
        if (request.CategorySlug is not null
            && (request.SkinType is not null || request.HairType is not null || request.Domain == "beauty"))
        {
            var explicitFilters = CopyFilters(request);
            explicitFilters.MaxPrice ??= writtenBudget ?? conversation.PendingBudgetRials;
            return new(
                message,
                explicitFilters,
                decision.Intent,
                request.ConcernSlug is null ? [] : [request.ConcernSlug],
                HasAny(text, "ارزان", "ارزون", "اقتصادی"),
                [],
                Source: "explicit-filters");
        }

        QueryModelOutput parsed;
        var source = "model";
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
            source = "explicit-filters";
            var fallbackText = previous is null ? message : previous + " " + message;
            var domain = request.Domain ?? ScopeDomain(fallbackText);
            var profileMatches = vocabulary.Profiles.Where(x => (domain is null || x.Kind == domain) && LabelScore(PersianText.Normalize(fallbackText), x.Name) > 0)
                .ToArray();
            parsed = new()
            {
                Query = fallbackText[..Math.Min(500, fallbackText.Length)],
                Domain = domain,
                SkinType = profileMatches.Length == 1 && profileMatches[0].Kind == "skin" ? profileMatches[0].Slug : null,
                HairType = profileMatches.Length == 1 && profileMatches[0].Kind == "hair" ? profileMatches[0].Slug : null,
                CategorySlug = ExplicitCategory(PersianText.Normalize(fallbackText), domain, vocabulary)
            };
        }

        var filters = MergeFilters(request, parsed, decision, conversation, vocabulary, writtenBudget);
        if (filters.MinPrice > filters.MaxPrice)
        {
            throw new ArgumentException("بودجه نوشته‌شده از حداقل قیمت انتخاب‌شده کمتر است.");
        }

        var concerns = request.ConcernSlug is not null ? new[] {
            request.ConcernSlug
        } : parsed.ConcernSlugs;
        concerns = concerns.Where(s => vocabulary.Concerns.Any(c => c.Slug == s && (filters.Domain is null || c.Domain == filters.Domain)))
            .Distinct()
            .ToArray();
        // Relative requests seek alternatives, rather than being pinned to the earlier product IDs.
        var budget = parsed.PricePreference == "budget"
            || HasAny(text, "ارزان", "ارزون", "اقتصادی", "ارزان ترش", "ارزون ترش");
        return new(
            parsed.Query,
            filters,
            decision.Intent,
            concerns,
            budget,
            ids,
            InStockOnly: decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
            Source: source);
    }

    public static bool IsValid(QueryModelOutput value, CatalogVocabulary vocabulary)
    {
        if (string.IsNullOrWhiteSpace(value.Query) || value.Query.Length > 500
            || value.PricePreference is not ("budget" or "neutral")
            || value.ConcernSlugs is null
            || value.ConcernSlugs.Length > 3
            || value.ExcludeIngredientSlugs is null
            || value.ExcludeIngredientSlugs.Length > 20
            || value.Domain is not (null or "skin" or "hair" or "beauty"))
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
            && !vocabulary.Categories.Any(x => x.Slug == value.CategorySlug && (value.Domain is null || x.Domain == value.Domain)))
        {
            return false;
        }

        if (value.BrandSlug is not null && !vocabulary.Brands.Any(x => x.Slug == value.BrandSlug))
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
        var hair = HasAny(text, "مو", "موی", "موهام", "موها", "شامپو", "نرم کننده", "کراتین");
        var beauty = HasAny(text, "رژ", "ریمل", "کانسیلر", "سایه", "خط چشم", "کرم پودر", "لاک", "رژگونه");
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
            "شامپو بدن");
        if (text.Contains("شامپو بدن"))
        {
            hair = false;
        }

        return (hair, beauty, skin) switch
        {
            (true, false, false) => "hair",
            (false, true, false) => "beauty",
            (false, false, true) => "skin",
            _ => null
        };
    }

    private static string? ExplicitCategory(string text, string? domain, CatalogVocabulary vocabulary)
    {
        var slug = domain switch
        {
            "skin" when HasAny(text, "ضدآفتاب", "ضد آفتاب") => "sun-screen",
            "skin" when text.Contains("مرطوب کننده") && HasAny(text, "کرم") => "face-moisturizer",
            "skin" when HasAny(text, "شوینده") => "gentle-cleanser",
            "hair" when text.Contains("بدون آبکشی") && HasAny(text, "کرم") => "leave-in",
            "beauty" when text.Contains("رژ لب") => "lipstick",
            "beauty" when HasAny(text, "ریمل") => "mascara",
            _ => null
        };
        return vocabulary.Categories.Any(x => x.Slug == slug) ? slug : null;
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
        var scope = request.Domain ?? ScopeDomain(message + " " + previous);
        var searchText = PersianText.Normalize(message + " " + previous);
        var scoped = vocabulary with
        {
            Categories = vocabulary.Categories.Where(x => scope is null || x.Domain == scope).Select(x => new
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
            ["domain"] = PipelinePrompts.NullableEnum(["skin", "hair", "beauty"]),
            ["skinType"] = PipelinePrompts.NullableEnum(scoped.Profiles.Where(x => x.Kind == "skin").Select(x => x.Slug)),
            ["hairType"] = PipelinePrompts.NullableEnum(scoped.Profiles.Where(x => x.Kind == "hair").Select(x => x.Slug)),
            ["categorySlug"] = PipelinePrompts.NullableEnum(scoped.Categories.Select(x => x.Slug)),
            ["brandSlug"] = PipelinePrompts.NullableEnum(scoped.Brands.Select(x => x.Slug)),
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
                intent = decision.Code,
                previousUserQuestion = previous,
                categories = scoped.Categories.Select(x => new[] { x.Slug, x.Name, x.Domain }),
                brands = scoped.Brands.Select(x => new[] { x.Slug, x.Name }),
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
            && (filters.Domain is null || filters.Domain == category.Domain))
        {
            filters.CategorySlug = category.Slug;
        }

        filters.SkinType ??= filters.Domain is null or "skin" ? parsed.SkinType : null;
        filters.HairType ??= filters.Domain is null or "hair" ? parsed.HairType : null;
        filters.BrandSlug ??= parsed.BrandSlug;
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
