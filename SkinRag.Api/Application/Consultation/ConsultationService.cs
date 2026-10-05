using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.AI;
using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Prompts;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Telemetry;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Application.Consultation;

public sealed class ConsultationService(
    InputGuard guard,
    IIntentClassifier classifier,
    IQueryBuilder queryBuilder,
    IProductRepository repository,
    IProductRetriever retriever,
    IChatClient chatClient,
    RecommendationValidator validator,
    ConversationStore conversations,
    IConsultationPerformanceSink performanceSink,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<ConsultationService> logger)
{
    public async Task<ConsultationResponse> AskAsync(ConsultationRequest request, CancellationToken ct)
    {
        using var modelCallScope = ModelCallTelemetry.Begin(out var modelCalls);
        var start = DateTime.Now;
        var httpContext = httpContextAccessor.HttpContext;
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        var timing = new ConsultationPerformanceLog
        {
            StartedAtLocal = start,
            ClientIpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(userAgent, 1000),
            BrowserName = DetectBrowser(userAgent),
            OperatingSystem = DetectOperatingSystem(userAgent),
            RequestPath = Truncate(httpContext?.Request.Path.Value, 512),
            HttpMethod = Truncate(httpContext?.Request.Method, 10),
            TraceIdentifier = Truncate(httpContext?.TraceIdentifier, 64)
        };
        var totalTimer = Stopwatch.StartNew();
        try
        {
            var response = await AskCoreAsync(request, ct, timing);
            timing.Outcome = "success";
            timing.Intent = response.Intent;
            timing.RetrievalMethod = response.RetrievalMethod;
            timing.ResponseMode = response.ResponseMode;
            timing.ProductCount = response.Products.Count;
            return response;
        }
        catch (Exception ex)
        {
            timing.Outcome = ex is OperationCanceledException ? "cancelled" : "error";
            timing.ErrorType = ex.GetType().Name;
            throw;
        }
        finally
        {
            if (modelCalls.Count > 0)
            {
                timing.ModelCallsJson = JsonSerializer.Serialize(modelCalls);
            }

            totalTimer.Stop();
            timing.CompletedAtLocal = DateTime.Now;
            timing.TotalMs = (long)totalTimer.Elapsed.TotalMilliseconds;
            performanceSink.Enqueue(timing);
        }
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string? DetectBrowser(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("EdgA/", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("EdgiOS/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase)) return "Opera";
        if (userAgent.Contains("SamsungBrowser/", StringComparison.OrdinalIgnoreCase)) return "Samsung Internet";
        if (userAgent.Contains("FxiOS/", StringComparison.OrdinalIgnoreCase)) return "Firefox iOS";
        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (userAgent.Contains("CriOS/", StringComparison.OrdinalIgnoreCase)) return "Chrome iOS";
        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)) return "Safari";
        if (userAgent.Contains("MSIE ", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Trident/", StringComparison.OrdinalIgnoreCase)) return "Internet Explorer";
        return "Other";
    }

    private static string? DetectOperatingSystem(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase)) return "iOS";
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase)) return "ChromeOS";
        if (userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "macOS";
        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        return "Other";
    }

    private async Task<ConsultationResponse> AskCoreAsync(ConsultationRequest request, CancellationToken ct,
        ConsultationPerformanceLog timing)
    {
        if (request.EffectiveQuestion.Length > 2000)
        {
            throw new InputRejectedException("TOO_LONG", "پیام باید حداکثر ۲۰۰۰ نویسه باشد.");
        }

        var message = InputNormalizer.Normalize(request.EffectiveQuestion);
        var input = guard.Validate(message);
        if (!input.IsValid)
        {
            throw new InputRejectedException(input.Code!, input.Message!);
        }

        var state = conversations.Read(request.ConversationId);
        if (!request.FiltersOnly && ConversationStore.IsRepeated(state, message))
        {
            throw new InputRejectedException("REPEATED_MESSAGE",
                "این پیام چند بار تکرار شده است؛ کمی صبر کنید یا پرسش را تغییر دهید.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("Consultation:TimeoutSeconds", 220)));
        ct = deadline.Token;
        var clientMessages = request.History.Where(h => h.Role == "user").TakeLast(2)
            .Select(h => InputNormalizer.Normalize(h.Content)).ToArray();
        var recentMessages = state.RecentUserMessages.Length > 0 ? state.RecentUserMessages : clientMessages;
        var intentContext = new IntentContext(recentMessages, state.UserQuestions,
            state.UserQuestions.Length > 0 || state.ProductIds.Length > 0);
        var intent = await MeasureAsync(
            () => request.FiltersOnly
                ? Task.FromResult(new IntentDecision(ConsultationIntent.ProductSearch, 1, "filters-only"))
                : classifier.ClassifyAsync(message, intentContext, ct),
            elapsed => timing.IntentMs = elapsed);
        if (intent.Intent == ConsultationIntent.Unclear
            && intent.Source != "definition-only-catalog-phrase"
            && HasCatalogSelection(request)
            && IsRecommendationRequest(message))
        {
            intent = new IntentDecision(ConsultationIntent.ProductSearch, 1, "catalog-filter-rule");
        }

        logger.LogInformation(
            "Intent {Intent} ({Source}), confidence {Confidence:F2}, topic {Topic}, requires context {RequiresContext}",
            intent.Code, intent.Source, intent.Confidence, intent.ConversationTopic, intent.RequiresContext);
        timing.Intent = intent.Code;
        timing.IntentSource = intent.Source;

        ConsultationResponse Direct(string answer, string mode, bool more = false, string? followUp = null,
            string? notice = null)
        {
            return new ConsultationResponse(
                answer,
                [],
                "IRR",
                "none",
                0,
                null,
                mode,
                notice,
                intent.Code,
                intent.Confidence,
                state.Id,
                more,
                followUp,
                [],
                intent.ConversationTopic?.ToString(),
                intent.Clarification?.ToString());
        }

        if (intent.Source == "moisturizer-texture-comparison-rule")
        {
            return Direct(
                "برای پوست چرب، ژل مرطوب‌کننده معمولاً بافت سبک‌تری دارد و انتخاب راحت‌تری است. اگر پوستتان با کرم بهتر کنار می‌آید، کرم هم می‌تواند مناسب باشد؛ کاتالوگ فقط نوع محصول را ثبت می‌کند و بافت هر فرمول را جداگانه تأیید نمی‌کند.",
                "consultation");
        }

        if (!intent.IsRelevant)
        {
            if (intent.Intent is ConsultationIntent.Greeting or ConsultationIntent.SmallTalk)
            {
                var topic = intent.ConversationTopic ?? (intent.Intent == ConsultationIntent.Greeting
                    ? ConversationTopic.Greeting
                    : ConversationTopic.CasualChat);
                conversations.SaveConversation(state, message);
                return Direct(ConversationReplies.ReplyForTopic(topic), "conversation");
            }

            var clarification = ConversationReplies.ClarificationFor(intent.Clarification);
            var budget = BudgetParser.Parse(message);
            var budgetNeedsProductClarification = intent.Clarification is ClarificationKind.ProductType
                or ClarificationKind.BudgetCurrency or ClarificationKind.BudgetAmount;
            if (budget.IsBudgetOnly && (budget.MaximumPriceRials.HasValue || request.MaxPrice.HasValue)
                                    && intent.Intent == ConsultationIntent.Unclear && budgetNeedsProductClarification)
            {
                var maximumPrice = request.MaxPrice ?? budget.MaximumPriceRials!.Value;
                var minimumPrice = request.MinPrice ?? budget.MinimumPriceRials;
                if (minimumPrice is { } minimumValue && minimumValue > maximumPrice)
                {
                    var boundsQuestion =
                        "حداقل قیمت از سقف بودجهٔ انتخاب‌شده بیشتر است؛ حداقل قیمت یا سقف بودجه را تغییر می‌دهید؟";
                    return Direct(boundsQuestion, "clarification", true, boundsQuestion);
                }

                conversations.SaveBudget(state, message, maximumPrice, minimumPrice);
                var rangeText = minimumPrice is { } minimumForDisplay
                    ? $"بین {minimumForDisplay.ToString("N0", CultureInfo.InvariantCulture)} تا {maximumPrice.ToString("N0", CultureInfo.InvariantCulture)} ریال"
                    : $"{maximumPrice.ToString("N0", CultureInfo.InvariantCulture)} ریال";
                var confirmation = $"بودجه شما {rangeText} در نظر گرفته شد.\n{clarification}";
                return Direct(confirmation, "clarification", true, clarification);
            }

            return intent.Intent switch
            {
                ConsultationIntent.Unclear when intent.Source == "unsupported-no-rinse-dry-shampoo" => Direct(
                    "در کاتالوگ فعلی شامپوی خشکِ بدون نیاز به آبکشی ثبت نشده است؛ دستهٔ «شامپو موی خشک» مربوط به موی خشک است و شامپوی بدون آبکشی نیست.",
                    "catalog-gap"),
                ConsultationIntent.OffTopic => Direct(
                    "من درباره محصولات پوست، مو و زیبایی پاسخ می‌دهم. لطفاً پرسشی در همین زمینه بنویسید.", "off-topic"),
                ConsultationIntent.Unsafe => Direct(
                    intent.Source == "medical-infection-rule"
                        ? "برای جوش عفونی نمی‌توانم از راه دور کرم درمانی تجویز کنم. اگر درد، ترشح چرکی، گسترش قرمزی یا تب دارید، با پزشک یا داروساز مشورت کنید. برای مراقبت روزمرهٔ غیر درمانی می‌توانم محصولات ملایم کاتالوگ را پیدا کنم."
                        : "برای این درخواست نمی‌توانم راهنمایی بدهم. می‌توانم اطلاعات ثبت‌شده و روش مصرف محصولات پوست، مو و زیبایی را بررسی کنم.",
                    "unsafe"),
                _ => Direct(clarification, "clarification",
                    true,
                    clarification,
                    intent.Source == "classifier-unavailable"
                        ? "سرویس تشخیص درخواست موقتاً پاسخ نداد؛ لطفاً دوباره تلاش کنید."
                        : null)
            };
        }

        var vocabulary = await MeasureAsync(() => repository.VocabularyAsync(ct),
            elapsed => timing.CatalogReadMs = elapsed);
        var plan = await MeasureAsync(() => queryBuilder.BuildAsync(request, message, intent, state, vocabulary, ct),
            elapsed => timing.QueryBuildMs = elapsed);
        timing.QuerySource = plan.Source;
        timing.ResolvedDomain = ResolveLogDomain(plan.Filters, vocabulary.Categories);
        timing.ResolvedCategorySlug = SingleOrSole(plan.Filters.CategorySlug, plan.Filters.CategorySlugs);
        timing.ResolvedBrandSlug = SingleOrSole(plan.Filters.BrandSlug, plan.Filters.BrandSlugs);
        if (configuration.GetValue("Telemetry:LogSearchQuery", true))
        {
            timing.SearchQuery = plan.Query;
        }

        if (plan.NeedsMoreInformation)
        {
            return Direct(plan.FollowUpQuestion!, "clarification", true, plan.FollowUpQuestion);
        }

        var retrieval = await retriever.RetrieveAsync(plan, ct);
        timing.SqlFilterMs = retrieval.SqlFilterMs;
        timing.EmbeddingMs = retrieval.EmbeddingMs;
        timing.ProductLoadMs = retrieval.ProductLoadMs;
        timing.RetrievalDiagnosticsJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = 2,
            plan.Source,
            Intent = intent.Code,
            IntentSource = intent.Source,
            request.FiltersOnly,
            ExplicitFilters = FilterSnapshot(request),
            ResolvedFilters = FilterSnapshot(plan.Filters),
            EffectiveFilters = retrieval.EffectivePlan is null
                ? null
                : FilterSnapshot(retrieval.EffectivePlan.Filters),
            // Keep the original top-level JSON keys for existing log queries.
            plan.Filters.Domain,
            plan.Filters.CategorySlug,
            plan.Filters.Shade,
            plan.Filters.Shades,
            plan.Filters.Finish,
            plan.Filters.Finishes,
            InferredConcernSlugs = plan.InferredConcernSlugs,
            ConcernSlugs = plan.InferredConcernSlugs,
            InferredProfileSlugs = plan.InferredProfileSlugs ?? [],
            plan.ProductIds,
            plan.InStockOnly,
            retrieval.EligibleProducts,
            EligibleProductIds = retrieval.EligibleProductIds ?? [],
            ReturnedProductIds = retrieval.Products.Select(x => x.Product.Id).ToArray(),
            retrieval.Diagnostics
        });

        ConsultationResponse Result(
            string answer,
            IReadOnlyList<ProductMatch> products,
            string mode,
            string? notice = null,
            bool more = false,
            string? followUp = null)
        {
            if (mode == "no-results")
            {
                timing.NoResultStage ??= retrieval.Diagnostics?.Stage ?? "consultation";
                timing.NoResultCause ??= retrieval.Diagnostics?.Cause ?? "no-results";
                timing.FirstRestoringFilter ??= retrieval.Diagnostics?.FirstRestoringFilter;
            }

            return new ConsultationResponse(
                answer,
                products,
                "IRR",
                retrieval.Method,
                retrieval.EligibleProducts,
                retrieval.IndexUpdatedAtUtc,
                mode,
                notice,
                intent.Code,
                intent.Confidence,
                state.Id,
                more,
                followUp,
                products.Where(p => p.Reason is not null)
                    .Select(p => new ProductRecommendation(p.Product.Id, p.Reason!)).ToArray(),
                intent.ConversationTopic?.ToString());
        }

        ConsultationResponse Finish(ConsultationResponse response)
        {
            if (response.Products.Count > 0)
            {
                conversations.Save(state, message, response.Products.Select(p => p.Product.Id),
                    retrieval.EffectivePlan ?? plan);
            }

            return response;
        }

        var effectivePlan = retrieval.EffectivePlan ?? plan;
        var retrievalNotice = retrieval.Diagnostics?.Cause == "requested-brand-unavailable-alternatives"
            ? "محصول برند درخواستی موجود نبود؛ گزینه‌های نمایش‌داده‌شده از برندهای دیگرند."
            : null;
        var responseNotice = plan.Notice ?? retrievalNotice;
        if (plan.InferredProfileSlugs?.Contains("hair-fine", StringComparer.Ordinal) == true
            && retrieval.Products.Count > 0
            && retrieval.Products.All(x => !x.Product.Profiles.Contains("hair-fine", StringComparer.Ordinal)))
        {
            const string fineHairNotice = "نوع موی نازک برای شامپوهای موجود در کاتالوگ ثبت نشده است؛ این گزینه‌ها شامپو هستند اما سازگاری‌شان با موی نازک تأیید نشده.";
            responseNotice = string.IsNullOrWhiteSpace(responseNotice)
                ? fineHairNotice
                : $"{responseNotice} {fineHairNotice}";
        }

        if (retrieval.Products.Count == 0)
        {
            var requestedCategorySlugs = plan.Filters.CategorySlugs.Append(plan.Filters.CategorySlug)
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var placeholderCategory = requestedCategorySlugs.FirstOrDefault(x => x!.StartsWith("src-cat-",
                StringComparison.OrdinalIgnoreCase));
            if (placeholderCategory is not null)
            {
                SetRetrievalCause(timing, "catalog-availability", "placeholder-only-category");
                var categoryName = vocabulary.Categories.FirstOrDefault(x => x.Slug == placeholderCategory)?.Name;
                var categoryLabel = string.IsNullOrWhiteSpace(categoryName) ? "این دسته" : $"دستهٔ «{categoryName}»";
                return Result($"{categoryLabel} در کاتالوگ ثبت شده، اما فعلاً محصول واقعیِ قابل پیشنهاد برای آن نداریم. رکورد جاینگهدار را نمایش نمی‌دهم.",
                    [], "no-results", responseNotice);
            }

            if (effectivePlan.Filters.IncludeIngredientSlugs.Length > 0)
            {
                var ingredientNames = vocabulary.Ingredients
                    .Where(x => effectivePlan.Filters.IncludeIngredientSlugs.Contains(x.Slug))
                    .Select(x => x.Name).ToArray();
                SetRetrievalCause(timing, "catalog-availability", "requested-ingredient-not-recorded-on-eligible-products");
                var ingredientList = string.Join("، ", ingredientNames);
                return Result($"در حال حاضر محصولی که ترکیب «{ingredientList}» آن در اطلاعات کاتالوگ ثبت شده باشد، با بقیهٔ درخواست شما پیدا نشد. می‌توانید ترکیب یا نوع محصول را تغییر دهید.",
                    [], "no-results", responseNotice);
            }

            if (effectivePlan.Filters.CategorySlug == "sun-screen"
                && effectivePlan.Filters.Shades.Length > 0
                && retrieval.Diagnostics?.FirstRestoringFilter == "shade")
            {
                SetRetrievalCause(timing, "catalog-availability", "requested-sunscreen-shade-not-recorded");
                return Result("در کاتالوگ برای ضدآفتاب‌ها رنگ ثبت نشده است؛ بنابراین نمی‌توانم روشن‌بودن رنگ را تأیید کنم یا ضدآفتاب بی‌رنگ را به‌جای آن پیشنهاد بدهم.",
                    [], "no-results", responseNotice);
            }

            return Result("محصول مرتبطی مطابق فیلترها و اطلاعات فعلی پیدا نشد. نوع محصول یا فیلترها را تغییر دهید.", [],
                "no-results", responseNotice);
        }

        if (intent.Intent is ConsultationIntent.PriceInquiry or ConsultationIntent.AvailabilityInquiry
            or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison)
        {
            var fresh = (await MeasureAsync(
                () => repository.LoadAsync(retrieval.Products.Select(x => x.Product.Id), effectivePlan, ct),
                elapsed => timing.ProductLoadMs = (timing.ProductLoadMs ?? 0) + elapsed)).ToDictionary(p => p.Id);
            var matches = retrieval.Products.Where(m => fresh.ContainsKey(m.Product.Id))
                .Take(intent.Intent == ConsultationIntent.ProductComparison ? 2 : 5)
                .Select(m => m with { Product = fresh[m.Product.Id] }).ToArray();
            if (matches.Length == 0)
            {
                SetRetrievalCause(timing, "live-product-load", "eligible-products-disappeared-before-detail-response");
                return Result("اطلاعات یا موجودی محصول تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
            }

            if (intent.Intent == ConsultationIntent.ProductComparison && matches.Length < 2)
            {
                return Result("برای مقایسه، نام یا شناسه دو محصول را مشخص کنید.", matches, "clarification", more: true,
                    followUp: "نام یا شناسه دو محصول موردنظر را می‌فرمایید؟");
            }

            return Finish(Result(ConsultationAnswerFormatter.InformationAnswer(matches, intent.Intent, vocabulary),
                matches, "catalog", responseNotice));
        }

        var context = retrieval.Products.Where(m => RecommendationValidator.CanRecommend(m.Product, effectivePlan.Filters))
            .Take(5).ToArray();
        if (context.Length == 0)
        {
            SetRetrievalCause(timing, "recommendation-eligibility", "validation-removed-all-retrieved-products");
            return Result("در حال حاضر محصول قابل پیشنهاد مطابق درخواست شما موجود نیست.", [], "no-results");
        }

        ValidatedConsultation? validated = null;
        var mode = "model";
        string? notice = null;
        var useGroundedFastPath = effectivePlan.Source is "explicit-category-rule"
            or "explicit-filter-rules" or "filters-only";
        if (useGroundedFastPath)
        {
            mode = "catalog";
        }
        else
        {
            try
            {
                var generated = await MeasureAsync(() => GenerateAnswerAsync(message, intent, context, ct),
                    elapsed => timing.AnswerGenerationMs = elapsed);
                validated = await MeasureAsync(() => validator.ValidateAsync(generated, context, effectivePlan, ct),
                    elapsed => timing.ValidationMs = elapsed);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                                       ex is OperationCanceledException or HttpRequestException
                                           or InvalidModelOutputException)
            {
                logger.LogWarning("Consultation generation unavailable ({ErrorType}); validating catalog fallback",
                    ex.GetType().Name);
            }
        }

        if (validated is null)
        {
            mode = "catalog";
            notice = responseNotice ?? (useGroundedFastPath
                ? null
                : "پاسخ مدل قابل استفاده نبود؛ نتیجه از اطلاعات فعلی کاتالوگ تهیه شد.");
            var fallback = new ConsultationResult
            {
                Answer = GroundedAnswers.Answers[0],
                Recommendations = context
                    .Select(x => new ProductRecommendation(x.Product.Id, GroundedAnswers.Reason(x.Product))).ToList()
            };
            validated = await MeasureAsync(
                () => validator.ValidateAsync(fallback, context, effectivePlan, ct),
                elapsed => timing.ValidationMs = (timing.ValidationMs ?? 0) + elapsed);
        }

        if (validated is null)
        {
            SetRetrievalCause(timing, "answer-validation", "no-valid-products-after-grounded-fallback");
            return Result("موجودی یا اطلاعات محصولات تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
        }

        var answer = validated.NeedsMoreInformation
            ? validated.Answer + "\n" + validated.FollowUpQuestion
            : validated.Answer;

        return Finish(Result(answer, validated.Products, mode, notice, validated.NeedsMoreInformation,
            validated.FollowUpQuestion));
    }

    private static object FilterSnapshot(CatalogFilters filters) => new
    {
        filters.Domain,
        filters.Domains,
        filters.CategorySlug,
        filters.CategorySlugs,
        filters.BrandSlug,
        filters.BrandSlugs,
        filters.ExcludedBrandSlugs,
        filters.SkinType,
        filters.SkinTypes,
        filters.HairType,
        filters.HairTypes,
        filters.ConcernSlug,
        filters.ConcernSlugs,
        filters.MinPrice,
        filters.MaxPrice,
        filters.Shade,
        filters.Shades,
        filters.Finish,
        filters.Finishes,
        filters.SizeValue,
        filters.SizeUnit,
        filters.FragranceFree,
        filters.ExcludeIngredientSlugs,
        filters.IncludeIngredientSlugs
    };

    private static string? SingleOrSole(string? single, string[] values)
    {
        if (!string.IsNullOrWhiteSpace(single)) return single;
        var selected = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }

    private static string? ResolveLogDomain(CatalogFilters filters, IReadOnlyList<Category> categories)
    {
        var selectedDomain = SingleOrSole(filters.Domain, filters.Domains);
        if (selectedDomain is not null)
        {
            return selectedDomain;
        }

        var selectedCategories = filters.CategorySlugs.Append(filters.CategorySlug)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domains = categories.Where(x => selectedCategories.Contains(x.Slug))
            .Select(x => x.Domain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return domains.Length == 1 ? domains[0] : null;
    }

    private static void SetRetrievalCause(ConsultationPerformanceLog timing, string stage, string cause)
    {
        timing.NoResultStage = stage;
        timing.NoResultCause = cause;
        var previous = timing.RetrievalDiagnosticsJson is null ? "null" : timing.RetrievalDiagnosticsJson;
        using var document = JsonDocument.Parse(previous);
        var merged = new Dictionary<string, JsonElement>();
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                merged[property.Name] = property.Value.Clone();
            }
        }

        merged["FinalNoResult"] = JsonSerializer.SerializeToElement(new { Stage = stage, Cause = cause });
        timing.FirstRestoringFilter ??= JsonSerializer.Deserialize<RetrievalDiagnosticsEnvelope>(previous)
            ?.Diagnostics?.FirstRestoringFilter;
        timing.RetrievalDiagnosticsJson = JsonSerializer.Serialize(merged);
    }

    private sealed record RetrievalDiagnosticsEnvelope(RetrievalDiagnostics? Diagnostics);

    private static async Task<T> MeasureAsync<T>(Func<Task<T>> operation, Action<double> record)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            return await operation();
        }
        finally
        {
            timer.Stop();
            record(timer.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<ConsultationResult> GenerateAnswerAsync(string message, IntentDecision intent,
        IReadOnlyList<ProductMatch> context, CancellationToken ct)
    {
        return await StructuredChatCompletion.GetAsync<ConsultationResult>(chatClient, configuration,
            PipelinePrompts.Consultation,
            new
            {
                message,
                intent = intent.Code,
                answerOptions = GroundedAnswers.AnswerCodes.Keys.ToArray(),
                followUpOptions = GroundedAnswers.FollowUpCodes.Keys.ToArray(),
                products = context.Select(m => new
                {
                    m.Product.Id,
                    m.Product.Name,
                    m.Product.Category,
                    allowedReason = GroundedAnswers.ReasonCode
                })
            },
            ConsultationSchema.Create(context),
            new ModelRequest("Consultation", configuration.GetValue("Consultation:AnswerTimeoutSeconds", 60),
                configuration.GetValue("Consultation:AnswerMaxTokens", 160)), ct);
    }

    private static bool HasCatalogSelection(ConsultationRequest request)
    {
        return request.Domains.Length > 0 || request.CategorySlugs.Length > 0 || request.BrandSlugs.Length > 0
               || request.SkinTypes.Length > 0 || request.HairTypes.Length > 0 || request.ConcernSlugs.Length > 0
               || request.Shades.Length > 0 || request.Finishes.Length > 0
               || request.CategorySlug is not null
               || request.BrandSlug is not null
               || request.SkinType is not null
               || request.HairType is not null
               || request.ConcernSlug is not null
               || request.MaxPrice.HasValue
               || request.MinPrice.HasValue
               || request.Shade is not null
               || request.Finish is not null
               || request.SizeValue.HasValue
               || request.FragranceFree.HasValue
               || request.ExcludeIngredientSlugs.Length > 0
               || request.IncludeIngredientSlugs.Length > 0;
    }

    private static bool IsRecommendationRequest(string message)
    {
        var text = PersianText.Normalize(message);
        return text.Contains("معرفی", StringComparison.Ordinal)
               || text.Contains("پیشنهاد", StringComparison.Ordinal)
               || text.Contains("نشون بده", StringComparison.Ordinal)
               || text.Contains("نشان بده", StringComparison.Ordinal)
               || text.Contains("پیدا کن", StringComparison.Ordinal)
               || text.Contains("میخوام", StringComparison.Ordinal)
               || text.Contains("میخواهم", StringComparison.Ordinal);
    }
}
