using System.Globalization;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Infrastructure.AI;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Models;
using SkinRag.Api.Prompts;
using SkinRag.Api.Services;

namespace SkinRag.Api.Application.Consultation;

public sealed class ConsultationService(InputGuard guard, IIntentClassifier classifier, IQueryBuilder queryBuilder,
    IProductRepository repository, IProductRetriever retriever, IOllamaClient ollama, RecommendationValidator validator,
    ConversationStore conversations, IConfiguration configuration, ILogger<ConsultationService> logger)
{
    public async Task<ConsultationResponse> AskAsync(ConsultationRequest request, CancellationToken ct)
    {
        if (request.EffectiveQuestion.Length > 2000) throw new InputRejectedException("TOO_LONG", "پیام باید حداکثر ۲۰۰۰ نویسه باشد.");
        var message = InputNormalizer.Normalize(request.EffectiveQuestion);
        var input = guard.Validate(message);
        if (!input.IsValid) throw new InputRejectedException(input.Code!, input.Message!);
        var state = conversations.Read(request.ConversationId);
        if (ConversationStore.IsRepeated(state, message))
            throw new InputRejectedException("REPEATED_MESSAGE", "این پیام چند بار تکرار شده است؛ کمی صبر کنید یا پرسش را تغییر دهید.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("Consultation:TimeoutSeconds", 220)));
        ct = deadline.Token;
        var priorQuestions = state.UserQuestions.Length > 0 ? state.UserQuestions : request.History
            .Where(h => h.Role == "user").TakeLast(2).Select(h => InputNormalizer.Normalize(h.Content)).ToArray();
        var intent = await classifier.ClassifyAsync(message, priorQuestions, ct);
        ConsultationResponse Direct(string answer, string mode, bool more = false, string? followUp = null, string? notice = null) =>
            new(answer, [], "IRR", "none", 0, null, false, mode, notice, intent.Code,
                intent.Confidence, state.Id, more, followUp, []);
        if (!intent.IsRelevant)
        {
            return intent.Intent switch
            {
                ConsultationIntent.Greeting => Direct(ConversationReplies.GetReply(message)
                    ?? "سلام! برای انتخاب محصولات پوست، مو و زیبایی کمکتان می‌کنم.", "conversation"),
                ConsultationIntent.OffTopic => Direct("من درباره محصولات پوست، مو و زیبایی پاسخ می‌دهم. لطفاً پرسشی در همین زمینه بنویسید.", "off-topic"),
                ConsultationIntent.Unsafe => Direct("برای این درخواست نمی‌توانم راهنمایی بدهم. می‌توانم اطلاعات ثبت‌شده و روش مصرف محصولات پوست، مو و زیبایی را بررسی کنم.", "unsafe"),
                _ => Direct("لطفاً نوع محصول یا نیازتان درباره پوست، مو و زیبایی را واضح‌تر بنویسید.", "clarification", true,
                    GroundedAnswers.FollowUps[2], intent.Source == "classifier-unavailable" ? "سرویس تشخیص درخواست موقتاً پاسخ نداد؛ لطفاً دوباره تلاش کنید." : null)
            };
        }

        var vocabulary = await repository.VocabularyAsync(ct);
        var plan = await queryBuilder.BuildAsync(request, message, intent, state, vocabulary, ct);
        if (plan.NeedsMoreInformation) return Direct(plan.FollowUpQuestion!, "clarification", true, plan.FollowUpQuestion);
        var retrieval = await retriever.RetrieveAsync(plan, ct);
        ConsultationResponse Result(string answer, IReadOnlyList<ProductMatch> products, string mode,
            string? notice = null, bool more = false, string? followUp = null) => new(answer, products, "IRR", retrieval.Method,
                retrieval.EligibleProducts, retrieval.IndexUpdatedAtUtc, products.Any(p => p.Product.IsDemo), mode, notice,
                intent.Code, intent.Confidence, state.Id, more, followUp,
                products.Where(p => p.Reason is not null).Select(p => new ProductRecommendation(p.Product.Id, p.Reason!)).ToArray());
        ConsultationResponse Finish(ConsultationResponse response)
        {
            if (response.Products.Count > 0) conversations.Save(state, message, response.Products.Select(p => p.Product.Id), plan);
            return response;
        }
        if (retrieval.Products.Count == 0)
            return Result("محصول مرتبطی مطابق فیلترها و اطلاعات فعلی پیدا نشد. نوع محصول یا فیلترها را تغییر دهید.", [], "no-results");

        if (intent.Intent is ConsultationIntent.PriceInquiry or ConsultationIntent.AvailabilityInquiry
            or ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison)
        {
            var fresh = (await repository.LoadAsync(retrieval.Products.Select(x => x.Product.Id), plan, ct)).ToDictionary(p => p.Id);
            var matches = retrieval.Products.Where(m => fresh.ContainsKey(m.Product.Id)).Take(2)
                .Select(m => m with { Product = fresh[m.Product.Id] }).ToArray();
            if (matches.Length == 0) return Result("اطلاعات یا موجودی محصول تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
            if (intent.Intent == ConsultationIntent.ProductComparison && matches.Length < 2)
                return Result("برای مقایسه، نام یا شناسه دو محصول را مشخص کنید.", matches, "clarification", more: true,
                    followUp: "نام یا شناسه دو محصول موردنظر را می‌فرمایید؟");
            return Finish(Result(InformationAnswer(matches, intent.Intent, vocabulary), matches, "catalog"));
        }

        var context = retrieval.Products.Where(m => RecommendationValidator.CanRecommend(m.Product, plan.Filters)).Take(2).ToArray();
        if (context.Length == 0) return Result("در حال حاضر محصول قابل پیشنهاد مطابق درخواست شما موجود نیست.", [], "no-results");
        ValidatedConsultation? validated = null;
        string mode = "model";
        string? notice = null;
        try
        {
            var reasons = new[] { GroundedAnswers.ReasonCode };
            var schema = PipelinePrompts.Schema(new
            {
                type = "object", additionalProperties = false,
                properties = new
                {
                    answer = new { type = "string", @enum = GroundedAnswers.AnswerCodes.Keys.ToArray() },
                    recommendations = new
                    {
                        type = "array", maxItems = 2, items = new
                        {
                            type = "object", additionalProperties = false,
                            properties = new { productId = new { type = "integer", @enum = context.Select(x => x.Product.Id).ToArray() }, reason = new { type = "string", @enum = reasons } },
                            required = new[] { "productId", "reason" }
                        }
                    },
                    needsMoreInformation = new { type = "boolean" }, followUpQuestion = PipelinePrompts.NullableEnum(GroundedAnswers.FollowUpCodes.Keys)
                },
                required = new[] { "answer", "recommendations", "needsMoreInformation", "followUpQuestion" }
            });
            var generated = await ollama.ChatStructuredAsync<ConsultationResult>(PipelinePrompts.Consultation, new
            {
                message, intent = intent.Code, answerOptions = GroundedAnswers.AnswerCodes.Keys.ToArray(),
                followUpOptions = GroundedAnswers.FollowUpCodes.Keys.ToArray(),
                products = context.Select(m => new
                {
                    m.Product.Id, m.Product.Name, m.Product.Category,
                    allowedReason = GroundedAnswers.ReasonCode
                })
            }, schema, new("Consultation", configuration.GetValue("Consultation:AnswerTimeoutSeconds", 60),
                configuration.GetValue("Consultation:AnswerMaxTokens", 160)), ct);
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
        if (validated is null) return Result("موجودی یا اطلاعات محصولات تغییر کرده است؛ دوباره جست‌وجو کنید.", [], "no-results");
        var answer = validated.NeedsMoreInformation ? validated.Answer + "\n" + validated.FollowUpQuestion
            : validated.Answer + "\n" + string.Join("\n", validated.Products.Select(x =>
                $"- [{x.Product.Id}] {x.Product.Name}؛ {Price(x.Product)} {x.Reason}"));
        if (validated.Products.Any(x => x.Product.IsDemo))
            answer += "\nقیمت و مشخصات این رکوردها هنوز با اطلاعات فروشنده تأیید نشده‌اند.";
        return Finish(Result(answer, validated.Products, mode, notice, validated.NeedsMoreInformation, validated.FollowUpQuestion));
    }

    private static string Price(ProductDto p) => p.Price.HasValue ? $"قیمت ثبت‌شده از {p.Price.Value.ToString("N0", CultureInfo.InvariantCulture)} ریال." : "قیمت ثبت نشده است.";
    private static string InformationAnswer(IEnumerable<ProductMatch> matches, ConsultationIntent intent, CatalogVocabulary vocabulary) =>
        string.Join("\n\n", matches.Select(m =>
        {
            var p = m.Product;
            var text = $"[{p.Id}] {p.Name}\n{Price(p)}\n" + (p.StockQuantity > 0 ? $"موجودی ثبت‌شده: {p.StockQuantity} عدد." : "در حال حاضر ناموجود است.");
            if (intent is ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison)
            {
                var ingredients = p.Ingredients.Select(s => vocabulary.Ingredients.FirstOrDefault(x => x.Slug == s)?.Name ?? s);
                var formula = p.Ingredients.Length > 0 ? string.Join("، ", ingredients) : p.IngredientsText ?? "فهرست ترکیبات ثبت نشده است.";
                text += $"\nدسته: {p.Category}\nترکیبات ثبت‌شده: {formula}";
                if (!string.IsNullOrWhiteSpace(p.SkinTypes)) text += $"\nنوع پوست ثبت‌شده: {p.SkinTypes}";
                if (!string.IsNullOrWhiteSpace(p.HairTypes)) text += $"\nنوع موی ثبت‌شده: {p.HairTypes}";
                if (!string.IsNullOrWhiteSpace(p.UsageInstructions)) text += $"\nروش مصرف: {p.UsageInstructions}";
                if (!string.IsNullOrWhiteSpace(p.Warnings)) text += $"\n{p.Warnings}";
            }
            if (p.IsDemo) text += "\nقیمت و مشخصات این رکورد هنوز با اطلاعات فروشنده تأیید نشده‌اند.";
            return text;
        }));
}
