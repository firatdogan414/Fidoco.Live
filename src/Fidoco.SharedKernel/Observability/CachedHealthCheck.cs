using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fidoco.SharedKernel.Observability;

/// <summary>
/// Base for checks of external services (Stripe, Mux): the result is cached for a minute so health
/// probes do not hammer the provider, and failures report Degraded (never Unhealthy) so the hosting
/// platform does not restart the app because a third party has problems.
/// </summary>
public abstract class CachedExternalHealthCheck(IMemoryCache cache) : IHealthCheck
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    protected abstract string Name { get; }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync($"health:{Name}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            try
            {
                return await ProbeAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HealthCheckResult.Degraded($"{Name} unreachable: {ex.GetType().Name}");
            }
        });

    /// <summary>Returns Healthy, or Degraded with a reason. Exceptions count as Degraded.</summary>
    protected abstract Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken);
}
