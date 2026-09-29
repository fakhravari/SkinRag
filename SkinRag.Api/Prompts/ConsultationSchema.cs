using System.Text.Json;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Models;

namespace SkinRag.Api.Prompts;

internal static class ConsultationSchema
{
    public static JsonElement Create(IReadOnlyList<ProductMatch> context)
    {
        var reasons = new[] {
            GroundedAnswers.ReasonCode
        };
        return PipelinePrompts.Schema(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                answer = new
                {
                    type = "string",
                    @enum = GroundedAnswers.AnswerCodes.Keys.ToArray()
                },
                recommendations = new
                {
                    type = "array",
                    maxItems = 2,
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            productId = new
                            {
                                type = "integer",
                                @enum = context.Select(x => x.Product.Id).ToArray()
                            },
                            reason = new
                            {
                                type = "string",
                                @enum = reasons
                            }
                        },
                        required = new[] { "productId", "reason" }
                    }
                },
                needsMoreInformation = new { type = "boolean" },
                followUpQuestion = PipelinePrompts.NullableEnum(GroundedAnswers.FollowUpCodes.Keys)
            },
            required = new[] { "answer", "recommendations", "needsMoreInformation", "followUpQuestion" }
        });
    }
}
