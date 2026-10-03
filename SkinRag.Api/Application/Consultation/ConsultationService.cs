using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Application.Telemetry;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Domain.Catalog;
using SkinRag.Api.Application.Prompts;

namespace SkinRag.Api.Application.Consultation;

public sealed class ConsultationService(
    InputGuard guard,
    IIntentClassifier classifier,
    IQueryBuilder queryBuilder,
    IProductRepository repository,
    IProductRetriever retriever,
    IOllamaClient ollama,
    RecommendationValidator validator,
    ConversationStore conversations,
    IConsultationPerformanceSink performanceSink,
    IConfiguration configuration,
    ILogger<ConsultationService> logger)
{
    public async Task<ConsultationResponse> AskAsync(ConsultationRequest request, CancellationToken ct)
    {
        using var modelCallScope = ModelCallTelemetry.Begin(out var modelCalls);
        var start = DateTime.Now;
        var timing = new ConsultationPerformanceLog
        {
            StartedAtLocal = start
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

    private async Task<ConsultationResponse> AskCoreAsync(ConsultationRequest request, CancellationToken ct, ConsultationPerformanceLog timing)
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
            throw new InputRejectedException("REPEATED_MESSAGE", "این پیام چند بار تکرار شده است؛ کمی صبر کنید یا پرسش را تغییر دهید.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("Consultation:TimeoutSeconds", 220)));
        ct = deadline.Token;
        var clientMessages = request.History.Where(h => h.Role == "user").TakeLast(2).Select(h => InputNormalizer.Normalize(h.Content)).ToArray();
        var recentMessages = state.RecentUserMessages.Length > 0 ? state.RecentUserMessages : clientMessages;
        var intentContext = new IntentContext(recentMessages, state.UserQuestions, state.UserQuestions.Length > 0 || state.ProductIds.Length > 0);
        var intent = await MeasureAsync(
            () => request.FiltersOnly
                ? Task.FromResult(new IntentDecision(ConsultationIntent.ProductSearch, 1, "filters-only"))
                : classifier.ClassifyAsync(message, intentContext, ct),
            elapsed => timing.IntentMs = elapsed);
        if (intent.Intent == ConsultationIntent.Unclear
            && HasCatalogSelection(request)
            && IsRecommendationRequest(message))
        {
            intent = new(ConsultationIntent.ProductSearch, 1, "catalog-filter-rule");
        }
        logger.LogInformation("Intent {Intent} ({Source}), confidence {Confidence:F2}, topic {Topic}, requires context {RequiresContext}",
                                    intent.Code, intent.Source, intent.Confidence, intent.ConversationTopic, intent.RequiresContext);
        timing.Intent = intent.Code;
        ConsultationResponse Direct(string answer, string mode, bool more = false, string? followUp = null, string? notice = null) => new(
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
        if (!intent.IsRelevant)
        {
            if (intent.Intent is ConsultationIntent.Greeting or ConsultationIntent.SmallTalk)
            {
                var topic = intent.ConversationTopic ?? (intent.Intent == ConsultationIntent.Greeting ? ConversationTopic.Greeting : ConversationTopic.CasualChat);
                conversations.SaveConversation(state, message);
                return Direct(ConversationReplies.ReplyForTopic(topic), "conversation");
            }

            var clarification = ConversationReplies.ClarificationFor(intent.Clarification);
            var budget = BudgetParser.Parse(message);
            var budgetNeedsProductClarification = intent.Clarification is ClarificationKind.ProductType or ClarificationKind.BudgetCurrency or ClarificationKind.BudgetAmount;
            if (budget.IsBudgetOnly && (budget.MaximumPriceRials.HasValue || request.MaxPrice.HasValue)
                && intent.Intent == ConsultationIntent.Unclear && budgetNeedsProductClarification)
            {
                var maximumPrice = request.MaxPrice ?? budget.MaximumPriceRials!.Value;
                var minimumPrice = request.MinPrice ?? budget.MinimumPriceRials;
                if (minimumPrice is { } minimumValue && minimumValue > maximumPrice)
                {
                    var boundsQuestion = "حداقل قیمت از سقف بودجهٔ انتخاب‌شده بیشتر است؛ حداقل قیمت یا سقف بودجه را تغییر می‌دهید؟";
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
                ConsultationIntent.OffTopic => Direct("من درباره محصولات پوست، مو و زیبایی پاسخ می‌دهم. لطفاً پرسشی در همین زمینه بنویسید.", "off-topic"),
                ConsultationIntent.Unsafe => Direct("برای این درخواست نمی‌توانم راهنمایی بدهم. می‌توانم اطلاعات ثبت‌شده و روش مصرف محصولات پوست، مو و زیبایی را بررسی کنم.", "unsafe"),
                _ => Direct(clarification, "clarification",
                true,
                clarification,
                intent.Source == "classifier-unavailable" ? "سرویس تشخیص درخواست موقتاً پاسخ نداد؛ لطفاً دوباره تلاش کنید." : null)
            };
        }

        var vocabulary = await MeasureAsync(() => repository.VocabularyAsync(ct), elapsed => timing.CatalogReadMs = elapsed);
        var plan = await MeasureAsync(() => queryBuilder.BuildAsync(request, message, intent, state, vocabulary, ct), elapsed => timing.QueryBuildMs = elapsed);
        timing.QuerySource = plan.Source;
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
        ConsultationResponse Result(
            string answer,
            IReadOnlyList<ProductMatch> products,
            string mode,
            string? notice = null,
            bool more = false,
            string? followUp = null) => new(
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
            products.Where(p => p.Reason is not null).Select(p => new ProductRecommendation(p.Product.Id, p.Reason!)).ToArray(),
            intent.ConversationTopic?.ToString());
        ConsultationResponse Finish(ConsultationResponse response)
        {
            if (response.Products.Count > 0)
            {
                conversations.Save(state, message, response.Products.Select(p => p.Product.Id), plan);
            }

            return response;
        }

        if (retrieval.Products.Count == 0)
        {
            return Result("محصول مرتبطی مطابق فیلترها و اطلاعات فعلی پیدا نشد. نوع محصول یا فیلترها را تغییر دهید.", [], "no-results");
        }

        if (intent.Intent is ConsultationIntent.PriceInquiry or ConsultationIntent.AvailabilityInquiry or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison)
        {
            var fresh = (await MeasureAsync(() => repository.LoadAsync(retrieval.Products.Select(x => x.Product.Id), plan, ct), elapsed => timing.ProductLoadMs = (timing.ProductLoadMs ?? 0) + elapsed)).ToDictionary(p => p.Id);
            var matches = retrieval.Products.Where(m => fresh.ContainsKey(m.Product.Id)).Take(intent.Intent == ConsultationIntent.ProductComparison ? 2 : 5).Select(m => m with { Product = fresh[m.Product.Id] }).ToArray();
            if (matches.Length == 0)
            {
                return Result("اطلاعات یا موجودی محصول تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
            }

            if (intent.Intent == ConsultationIntent.ProductComparison && matches.Length < 2)
            {
                return Result("برای مقایسه، نام یا شناسه دو محصول را مشخص کنید.", matches, "clarification", more: true, followUp: "نام یا شناسه دو محصول موردنظر را می‌فرمایید؟");
            }

            return Finish(Result(ConsultationAnswerFormatter.InformationAnswer(matches, intent.Intent, vocabulary), matches, "catalog"));
        }

        var context = retrieval.Products.Where(m => RecommendationValidator.CanRecommend(m.Product, plan.Filters)).Take(5).ToArray();
        if (context.Length == 0)
        {
            return Result("در حال حاضر محصول قابل پیشنهاد مطابق درخواست شما موجود نیست.", [], "no-results");
        }

        ValidatedConsultation? validated = null;
        string mode = "model";
        string? notice = null;
        var useGroundedFastPath = plan.Source is "persian-product-rule" or "explicit-category-rule" or "explicit-filter-rules" or "filters-only";
        if (useGroundedFastPath)
        {
            mode = "catalog";
        }
        else
        {
            try
            {
                var generated = await MeasureAsync(() => GenerateAnswerAsync(message, intent, context, ct), elapsed => timing.AnswerGenerationMs = elapsed);
                validated = await MeasureAsync(() => validator.ValidateAsync(generated, context, plan, ct), elapsed => timing.ValidationMs = elapsed);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is (OperationCanceledException or HttpRequestException or InvalidModelOutputException))
            {
                logger.LogWarning("Consultation generation unavailable ({ErrorType}); validating catalog fallback", ex.GetType().Name);
            }
        }

        if (validated is null)
        {
            mode = "catalog";
            notice = useGroundedFastPath ? null : "پاسخ مدل قابل استفاده نبود؛ نتیجه از اطلاعات فعلی کاتالوگ تهیه شد.";
            var fallback = new ConsultationResult
            {
                Answer = GroundedAnswers.Answers[0],
                Recommendations = context.Select(x => new ProductRecommendation(x.Product.Id, GroundedAnswers.Reason(x.Product))).ToList()
            };
            validated = await MeasureAsync(
                () => validator.ValidateAsync(fallback, context, plan, ct),
                elapsed => timing.ValidationMs = (timing.ValidationMs ?? 0) + elapsed);
        }

        if (validated is null)
        {
            return Result("موجودی یا اطلاعات محصولات تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
        }

        var answer = validated.NeedsMoreInformation ? validated.Answer + "\n" + validated.FollowUpQuestion : validated.Answer;

        return Finish(Result(answer, validated.Products, mode, notice, validated.NeedsMoreInformation, validated.FollowUpQuestion));
    }

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

    private async Task<ConsultationResult> GenerateAnswerAsync(string message, IntentDecision intent, IReadOnlyList<ProductMatch> context, CancellationToken ct)
    {
        return await ollama.ChatStructuredAsync<ConsultationResult>(PipelinePrompts.Consultation,
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
            new("Consultation", configuration.GetValue("Consultation:AnswerTimeoutSeconds", 60), configuration.GetValue("Consultation:AnswerMaxTokens", 160)), ct);
    }

    private static bool HasCatalogSelection(ConsultationRequest request) =>
        request.CategorySlug is not null
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
        || request.ExcludeIngredientSlugs.Length > 0;

    private static bool IsRecommendationRequest(string message)
    {
        var text = SkinRag.Api.Application.Common.Text.PersianText.Normalize(message);
        return text.Contains("معرفی", StringComparison.Ordinal)
            || text.Contains("پیشنهاد", StringComparison.Ordinal)
            || text.Contains("نشون بده", StringComparison.Ordinal)
            || text.Contains("نشان بده", StringComparison.Ordinal)
            || text.Contains("پیدا کن", StringComparison.Ordinal)
            || text.Contains("میخوام", StringComparison.Ordinal)
            || text.Contains("میخواهم", StringComparison.Ordinal);
    }
}
