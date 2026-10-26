using Fidoco.SharedKernel.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Stripe;

namespace Fidoco.Modules.Payments.Infrastructure.Gateway;

/// <summary>Stripe reachable with our key (cheap balance read, cached a minute).</summary>
internal sealed class StripeHealthCheck(IMemoryCache cache, IOptions<StripeOptions> options, IOptions<PaymentsOptions> payments) : CachedExternalHealthCheck(cache)
{
    protected override string Name => "stripe";

    protected override async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        if (payments.Value.Provider == "Fake")
        {
            return HealthCheckResult.Healthy("Fake payment provider (development)");
        }

        if (string.IsNullOrEmpty(options.Value.SecretKey) || string.IsNullOrEmpty(options.Value.WebhookSecret))
        {
            return HealthCheckResult.Degraded("Stripe:SecretKey or Stripe:WebhookSecret is not configured");
        }

        await new BalanceService(new StripeClient(options.Value.SecretKey)).GetAsync(cancellationToken: cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
