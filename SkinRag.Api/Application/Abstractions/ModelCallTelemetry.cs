using System.Collections.Concurrent;

namespace SkinRag.Api.Application.Abstractions;

public sealed record ModelCallMetric(
    string Stage,
    string Model,
    double TotalMs,
    double LoadMs,
    int PromptTokens,
    double PromptEvaluationMs,
    int GeneratedTokens,
    double GenerationMs);

/// <summary>Collects Ollama calls made within one async consultation flow.</summary>
public static class ModelCallTelemetry
{
    private static readonly AsyncLocal<ConcurrentQueue<ModelCallMetric>?> CurrentCalls = new();

    public static IDisposable Begin(out IReadOnlyCollection<ModelCallMetric> calls)
    {
        var previous = CurrentCalls.Value;
        var current = new ConcurrentQueue<ModelCallMetric>();
        CurrentCalls.Value = current;
        calls = current;
        return new Scope(previous);
    }

    public static void Record(ModelCallMetric metric)
    {
        CurrentCalls.Value?.Enqueue(metric);
    }

    private sealed class Scope(ConcurrentQueue<ModelCallMetric>? previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentCalls.Value = previous;
        }
    }
}
