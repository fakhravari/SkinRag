namespace SkinRag.Api.Application.Telemetry;

public sealed class ConsultationPerformanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAtLocal { get; set; }
    public DateTime CompletedAtLocal { get; set; }
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
    public string? IntentSource { get; set; }
    public string? RetrievalMethod { get; set; }
    public string? ResponseMode { get; set; }
    public int? ProductCount { get; set; }
    public string? QuerySource { get; set; }
    public string? ModelCallsJson { get; set; }
    public string? SearchQuery { get; set; }
    public string? RetrievalDiagnosticsJson { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? BrowserName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? RequestPath { get; set; }
    public string? HttpMethod { get; set; }
    public string? TraceIdentifier { get; set; }
}
