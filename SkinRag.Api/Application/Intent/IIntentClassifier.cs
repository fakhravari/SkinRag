namespace SkinRag.Api.Application.Intent;

public interface IIntentClassifier
{
    Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct);

    Task<IntentDecision> ClassifyAsync(string message, IntentContext context, CancellationToken ct) =>
        ClassifyAsync(message, context.ProductQuestions, ct);
}
