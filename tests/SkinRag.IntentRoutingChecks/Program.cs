using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Domain.Catalog;

var chatClient = new ProbeChatClient();
var repository = new ProbeRepository();
var classifier = new IntentClassifier(chatClient, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:ChatModel"] = "test-model" }).Build(),
    NullLogger<IntentClassifier>.Instance, repository);
var context = new IntentContext([], [], false);

Check(PersianText.ContainsPhrase("می خواهم یک مرطوب‌کننده بخرم", "میخواهم"),
    "Separated and joined forms of می خواهم were not treated as equivalent.");
Check(PersianText.ContainsPhrase("کرم مرطوبکننده ارزان", "مرطوب کننده"),
    "Joined and separated forms of مرطوب کننده were not treated as equivalent.");
Check(!PersianText.ContainsPhrase("میخواهم کرم بخرم", "خواهم"),
    "A substring inside a token was incorrectly treated as a phrase match.");
Check(!PersianText.ContainsPhrase("مرطوبکننده ارزان", "مرطوب کننده پوست"),
    "A longer phrase incorrectly matched a shorter sentence.");
var blackheadSearch = CustomerLanguageQuery.AppendCatalogTerms("چطوری پاک کنم",
    "جوش سرسیاه منافذ پوست چطوری پاک کنم",
    [new CatalogPhrase { Phrase = "جوش سرسیاه", SearchTerms = "جوش سرسیاه منافذ", IsActive = true }]);
Check(blackheadSearch.Contains("جوش سرسیاه منافذ", StringComparison.Ordinal),
    "An active catalog phrase was not expanded inside the full blackhead question.");
var definitionOnlySearch = CustomerLanguageQuery.AppendCatalogTerms("ریزش مو",
    "ریزش مو",
    [new CatalogPhrase { Phrase = "ریزش مو", SearchTerms = "ریزش مو کم پشتی", MappingStatus = "DefinitionOnly", Definition = "ریزش مو", IsActive = true }]);
Check(definitionOnlySearch == "ریزش مو",
    "A definition-only phrase unexpectedly expanded a product-search query.");

chatClient.IntentCalls = 0;
var unsupportedPhrase = await classifier.ClassifyAsync("برای ریزش مو شامپو میخوام", context, default);
Check(unsupportedPhrase.Intent == ConsultationIntent.Unclear && chatClient.IntentCalls == 0,
    "A definition-only catalog phrase incorrectly entered product retrieval.");

var definition = await classifier.ClassifyAsync("پوست چرب چیست؟", context, default);
Check(definition.Intent == ConsultationIntent.Unclear
      && definition.Clarification == ClarificationKind.General
      && chatClient.IntentCalls == 1,
    "A definition question was short-circuited as a skin consultation.");

chatClient.IntentCalls = 0;
var symptom = await classifier.ClassifyAsync("پوستم چربه", context, default);
Check(symptom.Intent == ConsultationIntent.SkinConsultation && chatClient.IntentCalls == 0,
    "A direct customer symptom did not use the concern rule.");

chatClient.IntentCalls = 0;
var productRequest = await classifier.ClassifyAsync("پوست چرب چیست و چه کرمی پیشنهاد میدی؟", context, default);
Check(productRequest.Intent == ConsultationIntent.ProductSearch && chatClient.IntentCalls == 0,
    "An explicit product request was blocked by the definition-question guard.");

Console.WriteLine("10 text matching and intent routing checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class ProbeChatClient : IChatClient
{
    public int IntentCalls { get; set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IntentCalls++;
        var output = new IntentModelOutput
        {
            Intent = "UNCLEAR",
            Confidence = .95,
            Clarification = "General",
            ConversationTopic = null,
            RequiresContext = false
        };
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            JsonSerializer.Serialize(output, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
sealed class ProbeRepository : IProductRepository
{
    private static readonly IReadOnlyList<CatalogPhrase> Phrases =
    [
        new() { Phrase = "پوستم چربه", SearchTerms = "پوست چرب", MappingStatus = "Product", IsActive = true,
            Concern = new Concern { Name = "چربی پوست", SearchTerms = "پوستم چربه", Domain = "skin", Slug = "oily-skin" } },
        new() { Phrase = "ریزش مو", SearchTerms = "کم پشتی مو", MappingStatus = "DefinitionOnly", Definition = "کم پشتی و ریزش مو", IsActive = true }
    ];

    public Task<IReadOnlyList<CatalogPhrase>> IntentPhrasesAsync(CancellationToken ct) => Task.FromResult(Phrases);
    public Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct) =>
        throw new NotSupportedException();
}
