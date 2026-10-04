using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Domain.Catalog;

var ollama = new ProbeOllama();
var repository = new ProbeRepository();
var classifier = new IntentClassifier(ollama, new ConfigurationBuilder().Build(),
    NullLogger<IntentClassifier>.Instance, repository);
var context = new IntentContext([], [], false);

var definition = await classifier.ClassifyAsync("پوست چرب چیست؟", context, default);
Check(definition.Intent == ConsultationIntent.Unclear
      && definition.Clarification == ClarificationKind.General
      && ollama.IntentCalls == 1,
    "A definition question was short-circuited as a skin consultation.");

ollama.IntentCalls = 0;
var symptom = await classifier.ClassifyAsync("پوستم چربه", context, default);
Check(symptom.Intent == ConsultationIntent.SkinConsultation && ollama.IntentCalls == 0,
    "A direct customer symptom did not use the concern rule.");

ollama.IntentCalls = 0;
var productRequest = await classifier.ClassifyAsync("پوست چرب چیست و چه کرمی پیشنهاد میدی؟", context, default);
Check(productRequest.Intent == ConsultationIntent.ProductSearch && ollama.IntentCalls == 0,
    "An explicit product request was blocked by the definition-question guard.");

Console.WriteLine("3 intent routing checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class ProbeOllama : IOllamaClient
{
    public string EmbeddingModel => "test";
    public int IntentCalls { get; set; }

    public Task<T> ChatStructuredAsync<T>(string systemPrompt, object input, JsonElement schema,
        ModelRequest options, CancellationToken ct) where T : class
    {
        IntentCalls++;
        object output = new IntentModelOutput
        {
            Intent = "UNCLEAR",
            Confidence = .95,
            Clarification = "General"
        };
        return Task.FromResult((T)output);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Array.Empty<float>());

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Array.Empty<float[]>());
}

sealed class ProbeRepository : IProductRepository
{
    private static readonly IReadOnlyList<Concern> Concerns =
    [new() { Name = "پوست چرب", SearchTerms = "پوستم چربه", Domain = "skin", Slug = "oily-skin" }];

    public Task<IReadOnlyList<Concern>> IntentConcernsAsync(CancellationToken ct) => Task.FromResult(Concerns);
    public Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct) =>
        throw new NotSupportedException();
}
