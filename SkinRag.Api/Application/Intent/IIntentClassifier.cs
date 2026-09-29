namespace SkinRag.Api.Application.Intent;

public interface IIntentClassifier
{
    Task<IntentDecision> ClassifyAsync(string message, IReadOnlyList<string> previousQuestions, CancellationToken ct);
}
