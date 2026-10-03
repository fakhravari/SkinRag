using SkinRag.Api.Application.Telemetry;

namespace SkinRag.Api.Application.Abstractions;

public interface IConsultationPerformanceSink
{
    void Enqueue(ConsultationPerformanceLog item);
}
