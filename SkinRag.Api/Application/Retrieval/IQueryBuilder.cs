using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Models;

namespace SkinRag.Api.Application.Retrieval;

public interface IQueryBuilder
{
    Task<SearchPlan> BuildAsync(
                ConsultationRequest request,
                string message,
                IntentDecision decision,
                ConversationState conversation,
                CatalogVocabulary vocabulary,
                CancellationToken ct);
}
