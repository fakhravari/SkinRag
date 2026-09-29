using System.ComponentModel.DataAnnotations;

namespace SkinRag.Api.Models;

public sealed class ConsultationRequest : CatalogFilters
{
    [Required, MinLength(3), MaxLength(2000)]
    public string Question { get; set; } = "";

    [MaxLength(300)]
    public string? Concern { get; set; }

    [MaxLength(4)]
    public List<ChatTurn> History { get; set; } = [];
}

public sealed class ChatTurn
{
    [Required, RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = "";
    [Required, MaxLength(800)]
    public string Content { get; set; } = "";
}
