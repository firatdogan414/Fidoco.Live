using Fidoco.SharedKernel.Persistence;

namespace Fidoco.Host.Hosting;

/// <summary>Runs every module's data retention task once a day (and shortly after start).</summary>
internal sealed class RetentionService(IServiceScopeFactory scopes, TimeProvider time, IConfiguration configuration, ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Retention:Enabled", true))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                await RunOnceAsync(scopes, time, logger, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static async Task RunOnceAsync(IServiceScopeFactory scopes, TimeProvider time, ILogger logger, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IDataRetentionTask>())
        {
            try
            {
                var deleted = await task.RunAsync(time.GetUtcNow(), cancellationToken);
                logger.LogInformation("Retention {Task} deleted {Count} rows", task.GetType().Name, deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention {Task} failed", task.GetType().Name);
            }
        }
    }
}
