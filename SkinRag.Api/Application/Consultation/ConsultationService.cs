using System.Globalization;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Models;
using SkinRag.Api.Prompts;
using SkinRag.Api.Services;

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
    IConfiguration configuration,
    ILogger<ConsultationService> logger)
{
    public async Task<ConsultationResponse> AskAsync(ConsultationRequest request, CancellationToken ct)
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
        if (ConversationStore.IsRepeated(state, message))
        {
            throw new InputRejectedException("REPEATED_MESSAGE", "این پیام چند بار تکرار شده است؛ کمی صبر کنید یا پرسش را تغییر دهید.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("Consultation:TimeoutSeconds", 220)));
        ct = deadline.Token;
        var clientMessages = request.History.Where(h => h.Role == "user")
            .TakeLast(2).Select(h => InputNormalizer.Normalize(h.Content)).ToArray();
        var recentMessages = state.RecentUserMessages.Length > 0 ? state.RecentUserMessages : clientMessages;
        var intentContext = new IntentContext(recentMessages, state.UserQuestions,
            state.UserQuestions.Length > 0 || state.ProductIds.Length > 0);
        var intent = await classifier.ClassifyAsync(message, intentContext, ct);
        logger.LogInformation("Intent {Intent} ({Source}), confidence {Confidence:F2}, topic {Topic}, requires context {RequiresContext}",
            intent.Code, intent.Source, intent.Confidence, intent.ConversationTopic, intent.RequiresContext);
        ConsultationResponse Direct(string answer, string mode, bool more = false, string? followUp = null, string? notice = null) => new(
            answer,
            [],
            "IRR",
            "none",
            0,
            null,
            false,
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
                var topic = intent.ConversationTopic ?? (intent.Intent == ConsultationIntent.Greeting
                    ? ConversationTopic.Greeting : ConversationTopic.CasualChat);
                conversations.SaveConversation(state, message);
                return Direct(ConversationReplies.ReplyForTopic(topic), "conversation");
            }

            var clarification = ConversationReplies.ClarificationFor(intent.Clarification);
            var budget = BudgetParser.Parse(message);
            if (budget.IsBudgetOnly && budget.MaximumPriceRials is { } maximumPrice
                && intent.Intent == ConsultationIntent.Unclear && intent.Clarification == ClarificationKind.ProductType)
            {
                maximumPrice = request.MaxPrice ?? maximumPrice;
                conversations.SaveBudget(state, message, maximumPrice);
                var confirmation = $"بودجه شما {maximumPrice.ToString("N0", CultureInfo.InvariantCulture)} ریال در نظر گرفته شد.\n{clarification}";
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

        var vocabulary = await repository.VocabularyAsync(ct);
        var plan = await queryBuilder.BuildAsync(request, message, intent, state, vocabulary, ct);
        if (plan.NeedsMoreInformation)
        {
            return Direct(plan.FollowUpQuestion!, "clarification", true, plan.FollowUpQuestion);
        }

        var retrieval = await retriever.RetrieveAsync(plan, ct);
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
            products.Any(p => p.Product.IsDemo),
            mode,
            notice,
            intent.Code,
            intent.Confidence,
            state.Id,
            more,
            followUp,
            products.Where(p => p.Reason is not null).Select(p => new ProductRecommendation(p.Product.Id, p.Reason!)).ToArray(),
            intent.ConversationTopic?.ToString(),
            intent.Clarification?.ToString());
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
            var fresh = (await repository.LoadAsync(retrieval.Products.Select(x => x.Product.Id), plan, ct)).ToDictionary(p => p.Id);
            var matches = retrieval.Products.Where(m => fresh.ContainsKey(m.Product.Id)).Take(2).Select(m => m with { Product = fresh[m.Product.Id] }).ToArray();
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

        var context = retrieval.Products.Where(m => RecommendationValidator.CanRecommend(m.Product, plan.Filters)).Take(2).ToArray();
        if (context.Length == 0)
        {
            return Result("در حال حاضر محصول قابل پیشنهاد مطابق درخواست شما موجود نیست.", [], "no-results");
        }

        ValidatedConsultation? validated = null;
        string mode = "model";
        string? notice = null;
        try
        {
            var generated = await GenerateAnswerAsync(message, intent, context, ct);
            validated = await validator.ValidateAsync(generated, context, plan, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (OperationCanceledException or HttpRequestException or InvalidModelOutputException))
        {
            logger.LogWarning("Consultation generation unavailable ({ErrorType}); validating catalog fallback", ex.GetType().Name);
        }

        if (validated is null)
        {
            mode = "catalog";
            notice = "پاسخ مدل قابل استفاده نبود؛ نتیجه از اطلاعات فعلی کاتالوگ تهیه شد.";
            var fallback = new ConsultationResult
            {
                Answer = GroundedAnswers.Answers[0],
                Recommendations = context.Select(x => new ProductRecommendation(x.Product.Id, GroundedAnswers.Reason(x.Product))).ToList()
            };
            validated = await validator.ValidateAsync(fallback, context, plan, ct);
        }

        if (validated is null)
        {
            return Result("موجودی یا اطلاعات محصولات تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
        }

        var answer = validated.NeedsMoreInformation ? validated.Answer + "\n" + validated.FollowUpQuestion : validated.Answer + "\n" + string.Join("\n", validated.Products.Select(x => $"- [{x.Product.Id}] {x.Product.Name}؛ {ConsultationAnswerFormatter.Price(x.Product)} {x.Reason}"));
        if (validated.Products.Any(x => x.Product.IsDemo))
        {
            answer += "\nقیمت و مشخصات این رکوردها هنوز با اطلاعات فروشنده تأیید نشده‌اند.";
        }

        return Finish(Result(answer, validated.Products, mode, notice, validated.NeedsMoreInformation, validated.FollowUpQuestion));
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
}
