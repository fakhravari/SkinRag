using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.AI;
using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Common.Catalog;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Prompts;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Retrieval;

public sealed partial class QueryBuilder(
    IChatClient chatClient,
    IConfiguration configuration,
    ILogger<QueryBuilder> logger) : IQueryBuilder
{
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
            return new SearchPlan(
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
        var ruleDomain = request.Domain ?? ResolveDomain(text, vocabulary.Categories, vocabulary.CatalogPhrases);
        var ruleAttributeText = PersianText.Normalize($"{text} {request.Concern}");
        var candidateCategorySlug = decision.Intent == ConsultationIntent.ProductSearch
            // Prefer a catalog label the customer actually used over a broad rule.
            // For example, "سرم آبرسان" must stay a serum instead of being rewritten
            // to the general face-moisturizer category by the word "آبرسان".
            ? MatchUniqueCategory(ruleAttributeText, vocabulary.Categories, ruleDomain, vocabulary.CatalogPhrases)?.Slug
            : null;
        var ruleCategorySlug = candidateCategorySlug is not null
                               && (ruleDomain is null || CategoryBelongsToDomain(candidateCategorySlug, ruleDomain,
                                   vocabulary.Categories))
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
            && decision.Intent is ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison
                or ConsultationIntent.PriceInquiry or ConsultationIntent.AvailabilityInquiry)
        {
            ids = HasAny(text, "اولی") ? conversation.ProductIds.Take(1).ToArray() :
                HasAny(text, "دومی") ? conversation.ProductIds.Skip(1).Take(1).ToArray() : conversation.ProductIds;
        }

        if (isContextual && ids.Length == 0 && conversation.UserQuestions.Length == 0
            && !request.History.Any(x => x.Role == "user")
            && !HasAny(text, "پوست", "مو", "شامپو", "کرم", "ضدآفتاب", "رژ", "ریمل"))
        {
            return new SearchPlan(
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
            ApplyBudget(explicitFilters, budgetInput, conversation.PendingBudgetRials,
                conversation.PendingMinimumBudgetRials);
            return CheckBudgetBounds(new SearchPlan(
                message,
                explicitFilters,
                decision.Intent,
                request.ConcernSlug is null ? [] : [request.ConcernSlug],
                false,
                ids,
                decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry
                    or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
                Source: "product-reference"));
        }

        var previous = isContextual
            ? conversation.SearchQuery ?? conversation.UserQuestions.LastOrDefault() ??
            request.History.LastOrDefault(x => x.Role == "user")?.Content
            : null;
        if (request.FiltersOnly)
        {
            var terms = new List<string>();
            foreach (var selectedDomain in request.Domains.Append(request.Domain)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                terms.Add(vocabulary.Categories.FirstOrDefault(x => x.IdParent is null && x.Domain == selectedDomain)
                    ?.Name ?? selectedDomain!);
            }

            foreach (var categorySlug in request.CategorySlugs.Append(request.CategorySlug)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (vocabulary.Categories.FirstOrDefault(x => x.Slug == categorySlug) is { } category)
                    terms.Add(category.Name);
            }

            foreach (var skinType in request.SkinTypes.Append(request.SkinType)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (vocabulary.CatalogProfiles.FirstOrDefault(x => x.Slug == skinType) is { } skinProfile)
                    terms.Add(skinProfile.Name);
            }

            foreach (var hairType in request.HairTypes.Append(request.HairType)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (vocabulary.CatalogProfiles.FirstOrDefault(x => x.Slug == hairType) is { } hairProfile)
                    terms.Add(hairProfile.Name);
            }

            foreach (var brandSlug in request.BrandSlugs.Append(request.BrandSlug)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (vocabulary.Brands.FirstOrDefault(x => x.Slug == brandSlug) is { } brand)
                    terms.Add(brand.Name);
            }

            foreach (var concernSlug in request.ConcernSlugs.Append(request.ConcernSlug)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if (vocabulary.Concerns.FirstOrDefault(x => x.Slug == concernSlug) is { } concern)
                    terms.Add(concern.Name);
            }

            terms.AddRange(request.Shades.Append(request.Shade).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!));
            terms.AddRange(request.Finishes.Append(request.Finish).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!));
            if (request.FragranceFree == true) terms.Add("بدون عطر افزوده");

            if (terms.Count == 0)
            {
                terms.Add("محصولات مراقبتی");
            }

            var filterCopy = CopyFilters(request);
            ApplyBudget(filterCopy, budgetInput, conversation.PendingBudgetRials,
                conversation.PendingMinimumBudgetRials);
            return CheckBudgetBounds(new SearchPlan(
                string.Join(" ", terms),
                filterCopy,
                ConsultationIntent.ProductSearch,
                [],
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
        if (decision.Intent == ConsultationIntent.ProductSearch && hasRuleFilters)
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
                                       && ex is OperationCanceledException or HttpRequestException
                                           or InvalidModelOutputException)
            {
                logger.LogWarning(
                    "Query extraction unavailable ({ErrorType}); using explicit filters and customer wording",
                    ex.GetType().Name);
                source = $"explicit-filters:{ex.GetType().Name}";
                var fallbackText = previous is null ? message : previous + " " + message;
                var fallbackSearchText = $"{fallbackText} {CustomerLanguageQuery.ExpandTerms(fallbackText, vocabulary.CatalogPhrases)}";
                var domain = request.Domain ?? ResolveDomain(fallbackSearchText, vocabulary.Categories, vocabulary.CatalogPhrases);
                var profileMatches = vocabulary.CatalogProfiles.Where(x =>
                        (domain is null || x.Kind == domain) &&
                        LabelScore(PersianText.Normalize(fallbackSearchText), x.Name) > 0)
                    .ToArray();
                parsed = new QueryModelOutput
                {
                    Query = fallbackSearchText[..Math.Min(500, fallbackSearchText.Length)],
                    Domain = domain,
                    SkinType = profileMatches.Length == 1 && profileMatches[0].Kind == "skin"
                        ? profileMatches[0].Slug
                        : null,
                    HairType = profileMatches.Length == 1 && profileMatches[0].Kind == "hair"
                        ? profileMatches[0].Slug
                        : null,
                    CategorySlug = ExplicitCategory(PersianText.Normalize(fallbackSearchText), domain, vocabulary)
                };
            }
        }

        if (ruleDomain is not null)
        {
            parsed = ConstrainToDomain(parsed, ruleDomain, ruleCategorySlug, vocabulary.Categories);
        }

        var filters = MergeFilters(request, parsed, decision, conversation, vocabulary, writtenBudget);
        ApplyPhraseProfile(filters, message, vocabulary.CatalogPhrases);
        if (filters.SizeValue is null && explicitSize is not null)
        {
            filters.SizeValue = explicitSize.Value;
            filters.SizeUnit = explicitSize.Unit;
        }

        if (budgetInput.MinimumPriceRials is { } writtenMinimum && request.MinPrice is null)
        {
            filters.MinPrice = writtenMinimum;
        }
        else
        {
            filters.MinPrice ??= conversation.PendingMinimumBudgetRials;
        }

        var mappedConcerns = CatalogPhraseMatcher.FindProductMappings(message, vocabulary.CatalogPhrases)
            .Where(x => x.IdConcern is not null)
            .Select(x => x.Concern?.Slug)
            .Where(x => x is not null)
            .Cast<string>();
        var concerns = request.ConcernSlug is not null
            ? new[]
            {
                request.ConcernSlug
            }
            : parsed.ConcernSlugs.Concat(mappedConcerns).Distinct().ToArray();
        concerns = concerns.Where(s =>
                vocabulary.Concerns.Any(c => c.Slug == s && (filters.Domain is null || c.Domain == filters.Domain)))
            .Distinct()
            .ToArray();
        // Matte/glow is already an exact variant-level finish filter. Requiring the
        // corresponding product-level concern as well incorrectly excludes variants
        // whose products are tagged with lip-color but not matte-look/glow-look.
        if (request.ConcernSlug is null && filters.Domain == "beauty" && filters.Finish is not null)
        {
            concerns = concerns.Where(s => s is not ("matte-look" or "glow-look")).ToArray();
        }

        // A curly hair profile describes the customer's hair. It does not mean a
        // leave-in cream must also be tagged as a curl-styling product.
        if (request.ConcernSlug is null && filters.Domain == "hair"
                                        && filters.CategorySlug == "leave-in" && filters.HairType == "hair-curly")
        {
            concerns = concerns.Where(s => s != "curl-style").ToArray();
        }

        // Relative requests seek alternatives, rather than being pinned to the earlier product IDs.
        var budget = parsed.PricePreference == "budget" || IsBudgetRequest(text);
        return CheckBudgetBounds(new SearchPlan(
            CustomerLanguageQuery.AppendCatalogTerms(parsed.Query, message, vocabulary.CatalogPhrases),
            filters,
            decision.Intent,
            concerns,
            budget,
            ids,
            decision.Intent is not (ConsultationIntent.AvailabilityInquiry or ConsultationIntent.PriceInquiry
                or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison),
            Source: source));
    }

    [GeneratedRegex(@"(?:#|\[|شناسه\s*)([0-9]{1,10})(?:\]|\b)")]
    private static partial Regex ProductReferences();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>milliliters?|ml|میلی\s*لیتر|میلیلیتر|میل|grams?|g|گرم|pcs|عدد|تایی)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitSize();

    public static bool IsValid(QueryModelOutput value, CatalogVocabulary vocabulary)
    {
        if (string.IsNullOrWhiteSpace(value.Query) || value.Query.Length > 500
                                                   || value.PricePreference is not ("budget" or "neutral")
                                                   || value.ConcernSlugs is null
                                                   || value.ConcernSlugs.Length > 3
                                                   || value.ExcludeIngredientSlugs is null
                                                   || value.ExcludeIngredientSlugs.Length > 20
                                                   || (value.Domain is not null &&
                                                       !vocabulary.Categories.Any(x => x.Domain == value.Domain)))
        {
            return false;
        }

        if (value.SkinType is not null
            && !vocabulary.CatalogProfiles.Any(x => x.Kind == "skin" && x.Slug == value.SkinType))
        {
            return false;
        }

        if (value.HairType is not null
            && !vocabulary.CatalogProfiles.Any(x => x.Kind == "hair" && x.Slug == value.HairType))
        {
            return false;
        }

        if (value.CategorySlug is not null
            && !vocabulary.Categories.Any(x => x.Slug == value.CategorySlug
                                               && (value.Domain is null || CategoryBelongsToDomain(x.Slug, value.Domain,
                                                   vocabulary.Categories))))
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

    public static int[] ReferencedIds(string message)
    {
        return ProductReferences().Matches(message).Select(x => int.TryParse(x.Groups[1].Value, out var id) ? id : 0)
            .Where(x => x > 0)
            .Distinct()
            .Take(10)
            .ToArray();
    }

    public static decimal? ParseMaximumPrice(string text)
    {
        return BudgetParser.Parse(text).MaximumPriceRials;
    }

    public static CatalogFilters CopyFilters(CatalogFilters f)
    {
        return new CatalogFilters
        {
            Domain = f.Domain,
            Domains = f.Domains.ToArray(),
            CategorySlug = f.CategorySlug,
            CategorySlugs = f.CategorySlugs.ToArray(),
            BrandSlug = f.BrandSlug,
            BrandSlugs = f.BrandSlugs.ToArray(),
            SkinType = f.SkinType,
            SkinTypes = f.SkinTypes.ToArray(),
            HairType = f.HairType,
            HairTypes = f.HairTypes.ToArray(),
            ConcernSlug = f.ConcernSlug,
            ConcernSlugs = f.ConcernSlugs.ToArray(),
            MinPrice = f.MinPrice,
            MaxPrice = f.MaxPrice,
            Shade = f.Shade,
            Shades = f.Shades.ToArray(),
            Finish = f.Finish,
            Finishes = f.Finishes.ToArray(),
            SizeValue = f.SizeValue,
            SizeUnit = f.SizeUnit,
            FragranceFree = f.FragranceFree,
            ExcludeIngredientSlugs = f.ExcludeIngredientSlugs.ToArray()
        };
    }

    private static void ApplyBudget(CatalogFilters filters, ParsedBudget writtenBudget, decimal? pendingBudget,
        decimal? pendingMinimum)
    {
        filters.MinPrice ??= writtenBudget.MinimumPriceRials ?? pendingMinimum;
        filters.MaxPrice ??= writtenBudget.MaximumPriceRials ?? pendingBudget;
    }

    private static SearchPlan CheckBudgetBounds(SearchPlan plan)
    {
        return plan.Filters.MinPrice > plan.Filters.MaxPrice
            ? plan with
            {
                NeedsMoreInformation = true,
                FollowUpQuestion = "حداقل قیمت از سقف بودجه بیشتر است؛ حداقل قیمت یا سقف بودجه را تغییر می‌دهید؟",
                Source = "conflicting-price-bounds"
            }
            : plan;
    }

    private static bool HasAny(string text, params string[] values)
    {
        return values.Any(value => PersianText.ContainsPhrase(text, value));
    }

    private static int LabelScore(string text, string label)
    {
        string[] suffixes = ["", "ه", "م", "ها", "های", "ی", "تر", "تری"];
        var tokens = PersianText.SearchTokens(label).Where(t => t.Length >= 2
                                                               && t is not ("پوست" or "صورت" or "مو" or "موی" or "مراقبت"
                                                                   or "کننده" or "انواع" or "همه")).ToArray();
        if (tokens.Length > 1 && PersianText.ContainsPhrase(text, label))
        {
            return tokens.Length;
        }

        return tokens.Count(token => suffixes.Any(suffix => PersianText.ContainsPhrase(text, token + suffix)));
    }

    private static string? ExplicitCategory(string text, string? domain, CatalogVocabulary vocabulary)
    {
        var slug = MatchUniqueCategory(text, vocabulary.Categories, domain, vocabulary.CatalogPhrases)?.Slug;
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

    private static bool IsBudgetRequest(string text)
    {
        return HasAny(text,
            "ارزان", "ارزانتر", "ارزان تر", "ارزان قیمت", "قیمت ارزان", "ارزون", "ارزونتر", "ارزون تر",
            "اقتصادی", "به صرفه", "مقرون به صرفه", "قیمت مناسب");
    }

    private static CatalogProfile? MatchUniqueCatalogProfile(string text, IEnumerable<CatalogProfile> profiles)
    {
        var matches = profiles.Where(x => LabelScore(text, x.Name) > 0).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void ApplyPhraseProfile(CatalogFilters filters, string message,
        IEnumerable<CatalogPhrase>? mappings)
    {
        var matches = CatalogPhraseMatcher.FindProductMappings(message, mappings)
            .Where(x => x.CatalogProfile is not null)
            .OrderByDescending(x => PersianText.SearchTokens(x.Phrase).Count)
            .ThenByDescending(x => x.Priority)
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        var specificity = PersianText.SearchTokens(matches[0].Phrase).Count;
        var priority = matches[0].Priority;
        var profiles = matches.Where(x => PersianText.SearchTokens(x.Phrase).Count == specificity
                                          && x.Priority == priority)
            .Select(x => x.CatalogProfile!).DistinctBy(x => x.Id).ToArray();
        if (profiles.Length != 1)
        {
            return;
        }

        var profile = profiles[0];
        if (profile.Kind == "skin" && profile.Slug != "skin-all")
        {
            filters.SkinType ??= profile.Slug;
        }
        else if (profile.Kind == "hair" && profile.Slug != "hair-all")
        {
            filters.HairType ??= profile.Slug;
        }
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

    private static QueryModelOutput CreateRuleOutput(
        string text,
        string attributeText,
        string? domain,
        string? categorySlug,
        CatalogVocabulary vocabulary)
    {
        var profile = MatchUniqueCatalogProfile(attributeText,
            vocabulary.CatalogProfiles.Where(x => domain is null || x.Kind == domain));
        return new QueryModelOutput
        {
            Query = text,
            Domain = domain,
            CategorySlug = categorySlug,
            BrandSlug = MatchUniqueBrand(text, vocabulary.Brands)?.Slug,
            SkinType = profile?.Kind == "skin" ? profile.Slug : null,
            HairType = profile?.Kind == "hair" ? profile.Slug : null,
            ConcernSlugs = MatchExplicitConcerns(attributeText,
                vocabulary.Concerns.Where(x => domain is null || x.Domain == domain)),
            Shade = domain == "beauty" ? MatchExplicitCatalogValue(text, vocabulary.Shades) : null,
            Finish = domain == "beauty" ? MatchExplicitCatalogValue(text, vocabulary.Finishes) : null,
            FragranceFree = ParseFragrancePreference(text),
            ExcludeIngredientSlugs = MatchExcludedIngredients(text, vocabulary.Ingredients),
            PricePreference = IsBudgetRequest(text) ? "budget" : "neutral"
        };
    }

    private static string? ResolveDomain(string message, IEnumerable<Category> categories,
        IEnumerable<CatalogPhrase>? catalogPhrases = null)
    {
        var text = PersianText.Normalize(message);
        var all = categories.ToArray();
        var phraseMatch = MatchUniqueCategory(text, all, null, catalogPhrases);
        if (phraseMatch is not null)
        {
            return phraseMatch.Domain;
        }

        var mappedDomains = CatalogPhraseMatcher.FindProductMappings(text, catalogPhrases)
            .Select(x => new
            {
                Domain = x.Category?.Domain ?? x.Concern?.Domain ?? x.CatalogProfile?.Kind,
                Specificity = PersianText.SearchTokens(x.Phrase).Count,
                x.Priority
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain))
            .ToArray();
        if (mappedDomains.Length > 0)
        {
            var specificity = mappedDomains.Max(x => x.Specificity);
            var priority = mappedDomains.Where(x => x.Specificity == specificity).Max(x => x.Priority);
            var bestDomains = mappedDomains.Where(x => x.Specificity == specificity && x.Priority == priority)
                .Select(x => x.Domain!).Distinct(StringComparer.Ordinal).ToArray();
            if (bestDomains.Length == 1)
            {
                return bestDomains[0];
            }
        }

        var exact = all.Where(x => HasAny(text, PersianText.Normalize(x.Name))
                                   || HasAny(text, PersianText.Normalize(x.Slug.Replace('-', ' '))))
            .DistinctBy(x => x.Slug)
            .ToArray();
        if (exact.Length > 0)
        {
            var maximumSpecificity = exact.Max(x => PersianText.SearchTokens(x.Name).Count());
            var mostSpecific = exact.Where(x => PersianText.SearchTokens(x.Name).Count() == maximumSpecificity)
                .ToArray();
            if (mostSpecific.Length == 1)
            {
                return mostSpecific[0].Domain;
            }
        }

        return MatchUniqueCategory(text, all, null)?.Domain;
    }

    private static bool HasRuleFilters(QueryModelOutput parsed, ParsedSize? size)
    {
        return parsed.Domain is not null
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
    }

    private static bool HasExplicitRequestFilters(ConsultationRequest request)
    {
        return request.Domain is not null
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
               || request.ExcludeIngredientSlugs.Length > 0
               || request.Domains.Length > 0 || request.CategorySlugs.Length > 0 || request.BrandSlugs.Length > 0
               || request.SkinTypes.Length > 0 || request.HairTypes.Length > 0 || request.ConcernSlugs.Length > 0
               || request.Shades.Length > 0 || request.Finishes.Length > 0;
    }

    private static Category? MatchUniqueCategory(string text, IEnumerable<Category> categories, string? domain,
        IEnumerable<CatalogPhrase>? catalogPhrases = null)
    {
        var all = categories.ToArray();
        // A selected domain scopes category inference to categories actually
        // assigned to it. Ancestor-domain checks can pull a same-name category
        // from a different normalized domain into the match set.
        var candidates = all.Where(x => domain is null || x.Domain == domain).ToArray();
        var candidateIds = candidates.Select(x => x.Id).ToHashSet();
        var phraseMatches = CatalogPhraseMatcher.FindProductMappings(text, catalogPhrases)
            .Where(x => x.IdCategory is { } categoryId && candidateIds.Contains(categoryId)
                        && x.Category is not null)
            .OrderByDescending(x => PersianText.SearchTokens(x.Phrase).Count)
            .ThenByDescending(x => x.Priority)
            .ToArray();
        if (phraseMatches.Length > 0)
        {
            var bestLength = PersianText.SearchTokens(phraseMatches[0].Phrase).Count;
            var bestPriority = phraseMatches[0].Priority;
            var bestPhraseCategories = phraseMatches.Where(x => PersianText.SearchTokens(x.Phrase).Count == bestLength
                                                                 && x.Priority == bestPriority)
                .Select(x => x.Category!).DistinctBy(x => x.Id).ToArray();
            return bestPhraseCategories.Length == 1 ? bestPhraseCategories[0] : null;
        }

        var exact = candidates.Where(x => HasAny(text, PersianText.Normalize(x.Name))
                                          || HasAny(text, PersianText.Normalize(x.Slug.Replace('-', ' '))))
            .DistinctBy(x => x.Slug)
            .ToArray();
        if (exact.Length > 0)
        {
            var maxSpecificity = exact.Max(x => PersianText.SearchTokens(x.Name).Count());
            var mostSpecific = exact.Where(x => PersianText.SearchTokens(x.Name).Count() == maxSpecificity).ToArray();
            if (mostSpecific.Length == 1)
            {
                return mostSpecific[0];
            }

            // Duplicate labels often represent an imported parent and its
            // normalized leaf. Resolve toward the deepest category; equal-depth
            // duplicates remain ambiguous and are left for the model to clarify.
            var byId = all.ToDictionary(x => x.Id);

            int Depth(Category category)
            {
                var depth = 0;
                var current = category;
                var visited = new HashSet<int> { current.Id };
                while (current.IdParent is { } parentId && byId.TryGetValue(parentId, out var parent) &&
                       visited.Add(parent.Id))
                {
                    depth++;
                    current = parent;
                }

                return depth;
            }

            var maxDepth = mostSpecific.Max(Depth);
            var deepest = mostSpecific.Where(x => Depth(x) == maxDepth).ToArray();
            return deepest.Length == 1 ? deepest[0] : null;
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

            current = current.IdParent is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
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
                           ?? (parsed.CategorySlug is not null &&
                               CategoryBelongsToDomain(parsed.CategorySlug, domain, categories)
                               ? parsed.CategorySlug
                               : null);
        return new QueryModelOutput
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

    private static string[] MatchExcludedIngredients(string text, IEnumerable<Ingredient> ingredients)
    {
        return ingredients
            .Where(x => HasNegatedIngredientMention(text, x.Name)
                        || HasNegatedIngredientMention(text, x.InciName)
                        || HasNegatedIngredientMention(text, x.Slug.Replace('-', ' ')))
            .Select(x => x.Slug)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();
    }

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
                || HasAny(after, "نباشد", "نباشه", "نداشته باشد", "نداشته باشه", "نمیخوام", "نمی خواهم", "نمیخواهم",
                    "do not want"))
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
                if (!decimal.TryParse(rawValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                        out var value))
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
        var customerLanguageTerms = CustomerLanguageQuery.ExpandTerms(message, vocabulary.CatalogPhrases);
        var scopeText = $"{message} {previous} {customerLanguageTerms}";
        var scope = request.Domain ?? ResolveDomain(scopeText, vocabulary.Categories, vocabulary.CatalogPhrases);
        var searchText = PersianText.Normalize(scopeText);
        var scoped = vocabulary with
        {
            Categories = vocabulary.Categories
                .Where(x => scope is null || CategoryBelongsToDomain(x.Slug, scope, vocabulary.Categories)).Select(x =>
                    new
                    {
                        Value = x,
                        Score = LabelScore(searchText, x.Name)
                    })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(4)
                .Select(x => x.Value)
                .ToArray(),
            Concerns = vocabulary.Concerns.Where(x => scope is null || x.Domain == scope)
                .Where(x => LabelScore(searchText, x.Name + " " + x.SearchTerms) > 0)
                .Take(3)
                .ToArray(),
            CatalogProfiles = vocabulary.CatalogProfiles
                .Where(x => (scope is null || x.Kind == scope) && LabelScore(searchText, x.Name) > 0)
                .Take(4)
                .ToArray(),
            Brands = vocabulary.Brands.Where(x =>
                    PersianText.ContainsPhrase(searchText, x.Name)
                    || (x.Slug.StartsWith("ldora", StringComparison.Ordinal) && searchText.Contains("لدورا")))
                .ToArray(),
            Ingredients = vocabulary.Ingredients.Where(x => request.ExcludeIngredientSlugs.Contains(x.Slug)
                                                            || PersianText.ContainsPhrase(message, x.Name)
                                                            || message.Contains(x.Slug,
                                                                StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };

        object SlugArray(IEnumerable<string> values, int maximum)
        {
            return new
            {
                type = "array",
                maxItems = maximum,
                items = values.Any()
                    ? (object)new
                    {
                        type = "string",
                        @enum = values.ToArray()
                    }
                    : new
                    {
                        type = "string"
                    }
            };
        }

        var properties = new Dictionary<string, object>
        {
            ["query"] = new
            {
                type = "string",
                minLength = 1,
                maxLength = 500
            },
            ["domain"] =
                PipelinePrompts.NullableEnum(vocabulary.Categories.Select(x => x.Domain)
                    .Distinct(StringComparer.Ordinal)),
            ["skinType"] =
                PipelinePrompts.NullableEnum(scoped.CatalogProfiles.Where(x => x.Kind == "skin").Select(x => x.Slug)),
            ["hairType"] =
                PipelinePrompts.NullableEnum(scoped.CatalogProfiles.Where(x => x.Kind == "hair").Select(x => x.Slug)),
            ["categorySlug"] = PipelinePrompts.NullableEnum(scoped.Categories.Select(x => x.Slug)),
            ["brandSlug"] = PipelinePrompts.NullableEnum(scoped.Brands.Select(x => x.Slug)),
            ["shade"] = PipelinePrompts.NullableEnum(vocabulary.Shades ?? []),
            ["finish"] = PipelinePrompts.NullableEnum(vocabulary.Finishes ?? []),
            ["concernSlugs"] = SlugArray(scoped.Concerns.Select(x => x.Slug), Math.Min(3, scoped.Concerns.Length)),
            ["excludeIngredientSlugs"] = SlugArray(scoped.Ingredients.Select(x => x.Slug),
                Math.Min(20, scoped.Ingredients.Length)),
            ["fragranceFree"] = new
            {
                type = new[]
                {
                    "boolean",
                    "null"
                }
            },
            ["pricePreference"] = new
            {
                type = "string",
                @enum = new[]
                {
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
        var parsed = await StructuredChatCompletion.GetAsync<QueryModelOutput>(
            chatClient,
            configuration,
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
                profiles = scoped.CatalogProfiles.Select(x => new[] { x.Slug, x.Name, x.Kind }),
                concerns = scoped.Concerns.Select(x => new[] { x.Slug, x.Name }),
                ingredients = scoped.Ingredients.Select(x => new[] { x.Slug, x.Name })
            },
            schema,
            new ModelRequest(
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
                                         && (filters.Domain is null || CategoryBelongsToDomain(category.Slug,
                                             filters.Domain, vocabulary.Categories)))
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
        filters.ExcludeIngredientSlugs =
            filters.ExcludeIngredientSlugs.Concat(parsed.ExcludeIngredientSlugs).Distinct().ToArray();
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
            filters.ExcludeIngredientSlugs = filters.ExcludeIngredientSlugs.Concat(earlier.ExcludeIngredientSlugs)
                .Distinct().ToArray();
        }

        return filters;
    }

    private sealed record ParsedSize(decimal Value, string Unit);
}
