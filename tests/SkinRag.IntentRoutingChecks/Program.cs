using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.Catalog;
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
var highlighterCatalogPhrase = new CatalogPhrase { Phrase = "هایلایتر مات", IsActive = true };
Check(CatalogPhraseMatcher.MatchesFlexible("یک هایلایتر طلایی مات می خواهم", highlighterCatalogPhrase),
    "A variant term between category phrase words blocked flexible catalog matching.");
Check(!CatalogPhraseMatcher.MatchesFlexible("هایلایتر طلایی می خواهم", highlighterCatalogPhrase),
    "Flexible catalog matching accepted a missing finish word.");
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

chatClient.IntentCalls = 0;
var curlyHairRequest = await classifier.ClassifyAsync("برای موی فر و وز، کرم مو می‌خواهم", context, default);
Check(curlyHairRequest.Intent == ConsultationIntent.ProductSearch && chatClient.IntentCalls == 0,
    "A mapped curly-hair purchase request did not route to product search.");

chatClient.IntentCalls = 0;
var pinkMatteLipstick = await classifier.ClassifyAsync("یک رژ لب صورتی با جلوه مات می‌خواهم", context, default);
Check(pinkMatteLipstick.Intent == ConsultationIntent.ProductSearch && chatClient.IntentCalls == 0,
    "A lipstick purchase with variant attributes did not route to product search.");

chatClient.IntentCalls = 0;
var highlighterMatteWithOne = await classifier.ClassifyAsync("یک هایلایتر مات می‌خواهم", context, default);
Check(highlighterMatteWithOne.Intent == ConsultationIntent.ProductSearch && chatClient.IntentCalls == 0,
    "An explicit highlighter purchase with a leading quantity word entered the intent model.");

chatClient.IntentCalls = 0;
var highlighterGoldMatte = await classifier.ClassifyAsync("یک هایلایتر طلایی مات می خواهم", context, default);
Check(highlighterGoldMatte.Intent == ConsultationIntent.ProductSearch && chatClient.IntentCalls == 0,
    "A highlighter request with non-adjacent shade and finish terms did not route to product search.");

chatClient.IntentCalls = 0;
var highlighterDefinition = await classifier.ClassifyAsync("هایلایتر مات چیست؟", context, default);
Check(highlighterDefinition.Intent == ConsultationIntent.Unclear && chatClient.IntentCalls == 1,
    "A highlighter definition question was incorrectly routed to product search.");

Console.WriteLine("17 text matching and intent routing checks passed.");

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
        new() { Phrase = "ریزش مو", SearchTerms = "کم پشتی مو", MappingStatus = "DefinitionOnly", Definition = "کم پشتی و ریزش مو", IsActive = true },
        new() { Phrase = "موی فر", IdCatalogProfile = 10, MappingStatus = "Product", IsActive = true,
            CatalogProfile = new CatalogProfile { Id = 10, Name = "فر و مجعد", Kind = "hair", Slug = "hair-curly" } },
        new() { Phrase = "رژ لب", IdCategory = 42, MappingStatus = "Product", IsActive = true,
            Category = new Category { Id = 42, Name = "رژ لب", Domain = "beauty", Slug = "lipstick" } },
        new() { Phrase = "رژ لب صورتی", MappingStatus = "DefinitionOnly", Definition = "رژ لب صورتی", IsActive = true },
        new() { Phrase = "جلوه مات", MappingStatus = "DefinitionOnly", Definition = "جلوه مات", IsActive = true },
        new() { Phrase = "هایلایتر", IdCategory = 40, MappingStatus = "Product", IsActive = true,
            Category = new Category { Id = 40, Name = "هایلایتر", Domain = "beauty", Slug = "highlighter" } },
        new() { Phrase = "هایلایتر مات", IdCategory = 40, MappingStatus = "Product", IsActive = true,
            Category = new Category { Id = 40, Name = "هایلایتر", Domain = "beauty", Slug = "highlighter" } },
        new() { Phrase = "هایلایتر طلایی", IdCategory = 40, MappingStatus = "Product", IsActive = true,
            Category = new Category { Id = 40, Name = "هایلایتر", Domain = "beauty", Slug = "highlighter" } },
        new() { Phrase = "هایلایتر مات", MappingStatus = "DefinitionOnly", Definition = "هایلایتر مات", IsActive = true }
    ];
    public Task<IReadOnlyList<CatalogPhrase>> IntentPhrasesAsync(CancellationToken ct) => Task.FromResult(Phrases);
    public Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct) =>
        throw new NotSupportedException();
}
