using System.Diagnostics;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Chat;
using OllamaSharp.Models.Exceptions;
using SkinRag.Api.Application.Abstractions;

namespace SkinRag.Api.Infrastructure.Integrations.Ollama;

public sealed class OllamaTelemetryChatClient(
    IChatClient innerClient,
    ILogger<OllamaTelemetryChatClient> logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (OllamaException ex)
        {
            throw new HttpRequestException("Ollama chat request failed.", ex);
        }

        timer.Stop();
        var result = response.RawRepresentation as ChatDoneResponseStream;
        var stage = options?.AdditionalProperties?.TryGetValue("skinrag_stage", out var stageValue) == true
            ? stageValue?.ToString() ?? "unknown"
            : "unknown";
        var model = response.ModelId ?? options?.ModelId ?? "unknown";
        var totalMs = result is null ? timer.Elapsed.TotalMilliseconds : result.TotalDuration / 1e6;
        var loadMs = result?.LoadDuration / 1e6 ?? 0;
        var promptMs = result?.PromptEvalDuration / 1e6 ?? 0;
        var generationMs = result?.EvalDuration / 1e6 ?? totalMs;
        var promptTokens = result?.PromptEvalCount ?? checked((int)(response.Usage?.InputTokenCount ?? 0));
        var generatedTokens = result?.EvalCount ?? checked((int)(response.Usage?.OutputTokenCount ?? 0));

        logger.LogInformation(
            "Model {Stage}/{Model}: {PromptTokens} prompt tokens, {GeneratedTokens} generated tokens in {TotalMs:F1}ms",
            stage, model, promptTokens, generatedTokens, totalMs);
        ModelCallTelemetry.Record(new ModelCallMetric(stage, model, totalMs, loadMs, promptTokens,
            promptMs, generatedTokens, generationMs));
        return response;
    }
}
