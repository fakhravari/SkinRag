using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Contracts.Consultation;

public sealed record ConsultationResponse(
    string Answer,
    IReadOnlyList<ProductMatch> Products,
    string Currency,
    string RetrievalMethod,
    int EligibleProducts,
    DateTime? IndexUpdatedAtUtc,
    string ResponseMode = "model",
    string? Notice = null,
    string Intent = "PRODUCT_SEARCH",
    double IntentConfidence = 1,
    Guid? ConversationId = null,
    bool NeedsMoreInformation = false,
    string? FollowUpQuestion = null,
    IReadOnlyList<ProductRecommendation>? Recommendations = null,
    string? ConversationTopic = null,
    string? ClarificationKind = null);
