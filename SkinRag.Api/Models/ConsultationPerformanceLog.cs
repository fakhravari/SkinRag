namespace SkinRag.Api.Models;

public sealed class ConsultationPerformanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public long TotalMs { get; set; }
    public double? IntentMs { get; set; }
    public double? CatalogReadMs { get; set; }
    public double? QueryBuildMs { get; set; }
    public double? SqlFilterMs { get; set; }
    public double? EmbeddingMs { get; set; }
    public double? ProductLoadMs { get; set; }
    public double? AnswerGenerationMs { get; set; }
    public double? ValidationMs { get; set; }
    public string Outcome { get; set; } = "error";
    public string? ErrorType { get; set; }
    public string? Intent { get; set; }
    public string? RetrievalMethod { get; set; }
    public string? ResponseMode { get; set; }
    public int? ProductCount { get; set; }
}
