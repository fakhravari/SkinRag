using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Models;

namespace SkinRag.Api.Application.Validation;

public sealed record ValidatedConsultation(
        string Answer,
        IReadOnlyList<ProductMatch> Products,
        bool NeedsMoreInformation,
        string? FollowUpQuestion);
public sealed class RecommendationValidator(IProductRepository repository)
{
    public async Task<ValidatedConsultation?> ValidateAsync(
                ConsultationResult output,
                IReadOnlyList<ProductMatch> context,
                SearchPlan plan,
                CancellationToken ct)
    {
        var answer = GroundedAnswers.ResolveAnswer(output.Answer);
        var followUp = GroundedAnswers.ResolveFollowUp(output.FollowUpQuestion);
        if (answer is null || output.Recommendations is null || output.Recommendations.Count > 2)
        {
            return null;
        }

        if (output.NeedsMoreInformation)
        {
            return output.Recommendations.Count == 0 && followUp is not null ? new(answer, [], true, followUp) : null;
        }

        if (output.FollowUpQuestion is not null || output.Recommendations.Count == 0

            || output.Recommendations.Select(r => r?.ProductId).Distinct().Count() != output.Recommendations.Count)
        {
            return null;
        }

        var allowed = context.ToDictionary(x => x.Product.Id);
        if (output.Recommendations.Any(r => r is null || !allowed.TryGetValue(r.ProductId, out var match)

            || (r.Reason != GroundedAnswers.ReasonCode && r.Reason != GroundedAnswers.Reason(match.Product))))
        {
            return null;
        }

        var live = (await repository.LoadAsync(
output.Recommendations.Select(r => r.ProductId),
plan with
{
    InStockOnly = true
},
ct)).ToDictionary(p => p.Id);
        if (output.Recommendations.Any(r => !live.TryGetValue(r.ProductId, out var p) || !CanRecommend(p, plan.Filters)

            || (r.Reason != GroundedAnswers.ReasonCode && r.Reason != GroundedAnswers.Reason(p))))
        {
            return null;
        }

        return new(
                        answer,
                        output.Recommendations.Select(r =>
        {
            var match = allowed[r.ProductId];
            return match with
            {
                Product = live[r.ProductId],
                Reason = GroundedAnswers.Reason(live[r.ProductId])
            };
        })

                .ToArray(),
                        false,
                        null);
    }

    public static bool CanRecommend(ProductDto p, CatalogFilters filters) => p.StockQuantity > 0 && p.Price is >= 0 && p.Currency == "IRR"

        && (!filters.MaxPrice.HasValue || p.Price <= filters.MaxPrice)

        && (!filters.MinPrice.HasValue || p.Price >= filters.MinPrice)

        && (p.Variants.Count == 0

            || p.Variants.Any(v => v.StockQuantity > 0 && v.Price >= 0 && (!filters.MaxPrice.HasValue || v.Price <= filters.MaxPrice)

            && (!filters.MinPrice.HasValue || v.Price >= filters.MinPrice)

            && (filters.Shade is null || v.Shade == filters.Shade)

            && (filters.Finish is null || v.Finish == filters.Finish)

            && (!filters.SizeValue.HasValue || v.SizeValue == filters.SizeValue)

            && (filters.SizeUnit is null || v.SizeUnit == filters.SizeUnit)));
}
