using Fidoco.SharedKernel.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fidoco.SharedKernel.Outbox;

/// <summary>Background loop for one module's outbox: process until empty, then wait for a signal or the poll interval.</summary>
public sealed class OutboxProcessor<TContext>(
    OutboxDispatcher<TContext> dispatcher,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : ModuleDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var wakeUp = signal.NextSignal;
            try
            {
                while (await dispatcher.ProcessPendingAsync(stoppingToken) > 0)
                {
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox processing for {Module} failed", dispatcher.ModuleName);
            }

            try
            {
                await Task.WhenAny(wakeUp, Task.Delay(options.Value.PollInterval, stoppingToken));
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
