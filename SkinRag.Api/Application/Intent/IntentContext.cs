namespace SkinRag.Api.Application.Intent;

public sealed record IntentContext(IReadOnlyList<string> RecentUserMessages, IReadOnlyList<string> ProductQuestions, bool HasProductContext);
