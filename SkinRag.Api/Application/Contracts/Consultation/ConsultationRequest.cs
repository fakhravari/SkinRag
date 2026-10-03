using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Contracts.Consultation;

public sealed class ConsultationRequest : CatalogFilters
{
    [MaxLength(2000)] public string Question { get; set; } = "";

    [MaxLength(2000)] public string? Message { get; set; }

    public Guid? ConversationId { get; set; }
    public bool FiltersOnly { get; set; }

    [JsonIgnore]
    public string EffectiveQuestion => string.IsNullOrWhiteSpace(Question)
        ? Message ?? (FiltersOnly ? "محصولات مطابق فیلترهای انتخاب‌شده" : "")
        : Question;

    [MaxLength(300)] public string? Concern { get; set; }

    [MaxLength(4)] public List<ChatTurn> History { get; set; } = [];

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (string.IsNullOrWhiteSpace(EffectiveQuestion))
        {
            yield return new ValidationResult("پیام نمی‌تواند خالی باشد.", [nameof(Question), nameof(Message)]);
        }

        if (!string.IsNullOrWhiteSpace(Question) && !string.IsNullOrWhiteSpace(Message) && Question != Message)
        {
            yield return new ValidationResult("question و message نباید دو پیام متفاوت باشند.",
                [nameof(Question), nameof(Message)]);
        }

        if (History is null || History.Any(x => x is null || string.IsNullOrWhiteSpace(x.Content)))
        {
            yield return new ValidationResult("تاریخچه گفتگو نامعتبر است.", [nameof(History)]);
        }
    }
}

public sealed class ChatTurn
{
    [Required]
    [RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = "";

    [Required] [MaxLength(800)] public string Content { get; set; } = "";
}
