namespace SkinRag.Api.Infrastructure.Knowledge;

public sealed class KnowledgeIndexBackgroundService(
    KnowledgeIndexService index,
    IConfiguration configuration,
    ILogger<KnowledgeIndexBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await index.RebuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background knowledge refresh failed; previous snapshot retained");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Rag:RefreshIntervalSeconds", 300), 30,
                        86400)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
