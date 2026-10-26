using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fidoco.SharedKernel.Outbox;

public sealed record OutboxStats(int Pending, int Overdue, int Failed);

/// <summary>
/// Degraded when a side effect gave up (failed outbox messages need attention) or the backlog is old.
/// Never Unhealthy: the app keeps serving viewers.
/// </summary>
public sealed class OutboxHealthCheck(IEnumerable<IOutboxDispatcher> dispatchers) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var failed = 0;
        var overdue = 0;
        foreach (var dispatcher in dispatchers)
        {
            var stats = await dispatcher.GetStatsAsync(cancellationToken);
            data[dispatcher.ModuleName] = $"pending={stats.Pending} overdue={stats.Overdue} failed={stats.Failed}";
            failed += stats.Failed;
            overdue += stats.Overdue;
        }

        return failed > 0 || overdue > 0
            ? HealthCheckResult.Degraded($"{failed} failed and {overdue} overdue outbox messages", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
