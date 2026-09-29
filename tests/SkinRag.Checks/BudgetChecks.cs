using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Parsing;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;
using SkinRag.Api.Models;

internal static class BudgetChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var (message, expected) in new (string, decimal)[]
        {
            ("من 200 هزار تومان دارم", 2_000_000),
            ("من ۲۰۰ هزار تومان دارم", 2_000_000),
            ("من ٢٠٠ هزار تومان دارم", 2_000_000),
            ("سلام، من 200 هزار تومن دارم", 2_000_000),
            ("200 هزار تومان دارم", 2_000_000),
            ("من 200 هزار تومان پول دارم و کرم میخوام", 2_000_000),
            ("بودجه من تا 200 هزار تومان", 2_000_000),
            ("زیر 200 هزار تومان", 2_000_000),
            ("تا 200,000 تومان", 2_000_000),
            ("تا ۲۰۰٬۰۰۰ تومان", 2_000_000),
            ("من 200 هزار ریال دارم", 200_000),
            ("200 هزار ریال", 200_000),
            ("200000 ریال دارم", 200_000),
            ("تا 200 هزار ریال", 200_000),
            ("تا 1.5 میلیون ریال", 1_500_000),
            ("با بودجه ۲۰۰ هزار تومان کرم میخوام", 2_000_000)
        })
        {
            var result = BudgetParser.Parse(message);
            check(result.Status == BudgetStatus.Valid && result.MaximumPriceRials == expected,
                $"Budget conversion failed: {message}");
        }

        foreach (var (message, minimum, maximum) in new (string, decimal, decimal)[]
        {
            ("بین ۲۰۰ تا ۵۰۰ هزار تومان", 2_000_000, 5_000_000),
            ("از ۲۰۰ هزار تومان تا ۵۰۰ هزار تومان", 2_000_000, 5_000_000),
            ("بین 1.5 تا 2 میلیون ریال", 1_500_000, 2_000_000)
        })
        {
            var result = BudgetParser.Parse(message);
            check(result.Status == BudgetStatus.Valid && result.MinimumPriceRials == minimum
                && result.MaximumPriceRials == maximum, $"Budget range conversion failed: {message}");
        }

        foreach (var text in new[] { "زیر 200 هزار", "بودجه من 200 هزار", "من 200 هزار دارم" })
        {
            check(BudgetParser.Parse(text).Status == BudgetStatus.MissingCurrency,
                "Missing currency was guessed: " + text);
        }

        foreach (var text in new[] { "محصول شماره 200", "قیمت محصول 200 چنده", "200 ml کرم",
            "بعد از 200 روز", "میخوام 200 عدد", "کرم با 200 SPF", "پوست خشک دارم" })
        {
            check(BudgetParser.Parse(text).Status == BudgetStatus.None,
                "Non-budget text became a price limit: " + text);
        }

        check(BudgetParser.Parse("بودجه 200 هزار تومان تا 300 هزار تومان").Status == BudgetStatus.Ambiguous,
            "Two incompatible budgets were silently reduced to one");

        foreach (var text in new[] { "تا -200 هزار تومان", "تا 200 میلیارد تومان" })
        {
            try
            {
                BudgetParser.Parse(text);
                throw new InvalidOperationException("Invalid budget accepted: " + text);
            }
            catch (ArgumentException)
            {
                check(true, "Invalid budget was rejected");
            }
        }

        foreach (var text in new[] { "تا 1,00,000 تومان", "تا 1,000.000 تومان" })
        {
            try
            {
                BudgetParser.Parse(text);
                throw new InvalidOperationException("Malformed amount accepted: " + text);
            }
            catch (ArgumentException)
            {
                check(true, "Malformed amount was rejected");
            }
        }

        var configuration = new ConfigurationBuilder().Build();
        var model = new ProbeOllama();
        var classifier = new IntentClassifier(model, configuration, NullLogger<IntentClassifier>.Instance);
        using var store = new ConversationStore();
        var budgetOnly = "من 200 هزار تومان دارم";
        var decision = await classifier.ClassifyAsync(budgetOnly, new IntentContext([], [], false), default);
        check(decision.Intent == ConsultationIntent.Unclear && decision.Clarification == ClarificationKind.ProductType
            && model.Stages.Count == 0, "Budget-only message called the model or skipped product type");
        var withProduct = await classifier.ClassifyAsync(budgetOnly,
            new IntentContext(["کرم برای پوست خشک"], ["کرم برای پوست خشک"], true), default);
        check(withProduct.Intent == ConsultationIntent.FollowUp && model.Stages.Count == 0,
            "Budget follow-up lost earlier product intent");

        var repository = new ProbeRepository();
        var builder = new QueryBuilder(model, configuration, NullLogger<QueryBuilder>.Instance);
        var vocabulary = await repository.VocabularyAsync(default);
        var plan = await builder.BuildAsync(new ConsultationRequest { Question = "کرم با 200 هزار تومان" },
            "کرم با بودجه 200 هزار تومان", new(ConsultationIntent.ProductSearch, 1, "test"),
            store.Read(null), vocabulary, default);
        check(plan.Filters.MaxPrice == 2_000_000 && plan.Query.Length > 0,
            "Rial filter was not passed to product retrieval");
        var rangePlan = await builder.BuildAsync(new ConsultationRequest { Question = "کرم بین 200 تا 500 هزار تومان" },
            "کرم بین 200 تا 500 هزار تومان", new(ConsultationIntent.ProductSearch, 1, "test"),
            store.Read(null), vocabulary, default);
        check(rangePlan.Filters.MinPrice == 2_000_000 && rangePlan.Filters.MaxPrice == 5_000_000,
            "Written budget range did not become min/max filters");
        var conflictPlan = await builder.BuildAsync(new ConsultationRequest { Question = "کرم از 300 هزار تومان" , MinPrice = 4_000_000 },
            "کرم تا 300 هزار تومان", new(ConsultationIntent.ProductSearch, 1, "test"),
            store.Read(null), vocabulary, default);
        check(conflictPlan.NeedsMoreInformation && conflictPlan.Source == "conflicting-price-bounds",
            "Conflicting price bounds did not ask for clarification");
        var explicitPlan = await builder.BuildAsync(new ConsultationRequest
        {
            Question = "کرم با بودجه 200 هزار تومان",
            MaxPrice = 300_000,
            CategorySlug = "face-moisturizer",
            SkinType = "skin-dry"
        }, "کرم با بودجه 200 هزار تومان", new(ConsultationIntent.ProductSearch, 1, "test"),
            store.Read(null), vocabulary, default);
        check(explicitPlan.Filters.MaxPrice == 300_000,
            "User's explicit price filter was overridden by text");
        var missing = await builder.BuildAsync(new ConsultationRequest { Question = "کرم زیر 200 هزار" },
            "کرم زیر 200 هزار", new(ConsultationIntent.ProductSearch, 1, "test"),
            store.Read(null), vocabulary, default);
        check(missing.NeedsMoreInformation && missing.FollowUpQuestion!.Contains("ریال یا تومان"),
            "Missing currency did not ask for clarification");

        var service = new ConsultationService(new InputGuard(), classifier, builder, repository, null!, null!, null!,
            store, configuration, NullLogger<ConsultationService>.Instance);
        var first = await service.AskAsync(new ConsultationRequest { Question = budgetOnly }, default);
        check(first.NeedsMoreInformation && first.Answer.Contains("2,000,000 ریال")
            && first.Products.Count == 0 && first.ConversationId.HasValue,
            "Budget-only response did not confirm converted amount");
        var state = store.Read(first.ConversationId);
        check(state.PendingBudgetRials == 2_000_000 && state.UserQuestions.Length == 0,
            "Pending budget was not stored separately from product history");
        var next = await builder.BuildAsync(new ConsultationRequest { Question = "کرم پوست خشک میخوام" },
            "کرم پوست خشک میخوام", new(ConsultationIntent.ProductSearch, 1, "test"),
            state, vocabulary, default);
        check(next.Filters.MaxPrice == 2_000_000,
            "Confirmed budget did not carry into the next product request");
        store.Save(state, "کرم پوست خشک میخوام", [12], next);
        check(store.Read(state.Id).PendingBudgetRials is null, "Consumed budget remained pending");

        var rialsOnly = await service.AskAsync(new ConsultationRequest { Question = "من 200 هزار ریال دارم" }, default);
        check(rialsOnly.Answer.Contains("200,000 ریال") && store.Read(rialsOnly.ConversationId).PendingBudgetRials == 200_000,
            "Rial budget was multiplied or not stored");

        var rangeOnly = await service.AskAsync(new ConsultationRequest { Question = "بین 200 تا 500 هزار تومان" }, default);
        var rangeState = store.Read(rangeOnly.ConversationId);
        check(rangeState.PendingMinimumBudgetRials == 2_000_000 && rangeState.PendingBudgetRials == 5_000_000,
            "Budget-only range was not retained for the next product request");

        var explicitBudget = await service.AskAsync(new ConsultationRequest
        {
            Question = "بودجه 200 هزار تومان و سقف 300 هزار تومان",
            MaxPrice = 4_000_000
        }, default);
        check(store.Read(explicitBudget.ConversationId).PendingBudgetRials == 4_000_000,
            "Explicit UI budget did not override ambiguous amounts in a budget-only message");
    }
}
