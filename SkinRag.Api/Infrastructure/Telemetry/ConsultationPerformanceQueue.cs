using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Telemetry;
using SkinRag.Api.Infrastructure.Persistence;

namespace SkinRag.Api.Infrastructure.Telemetry;

public sealed class ConsultationPerformanceQueue(
    IDbContextFactory<SkinRagDbContext> dbFactory,
    ILogger<ConsultationPerformanceQueue> logger) : BackgroundService, IConsultationPerformanceSink
{
    private readonly Channel<ConsultationPerformanceLog> _queue = Channel.CreateBounded<ConsultationPerformanceLog>(
        new BoundedChannelOptions(5000)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });

    public void Enqueue(ConsultationPerformanceLog item)
    {
        if (!_queue.Writer.TryWrite(item))
        {
            logger.LogWarning("Consultation performance queue is full; request timing was not recorded");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<ConsultationPerformanceLog>(100);
                while (batch.Count < 100 && _queue.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                    db.ConsultationPerformanceLogs.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not persist consultation performance records");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
