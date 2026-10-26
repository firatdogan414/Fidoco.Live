using Fidoco.Contracts.Tenants;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Tenants.Integration;

internal sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Hosts without a tenant: platform admin and webhooks (e.g. admin.fidoco.se).</summary>
    public List<string> Hosts { get; set; } = [];
}

/// <summary>Resolves the tenant from the Host header and sets <see cref="ITenantContext"/> for the request.</summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<PlatformOptions> platform)
{
    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContext tenantContext)
    {
        var host = TenantDirectory.NormalizeHost(context.Request.Host.Host);
        if (platform.Value.Hosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            context.MarkPlatformHost();
            await next(context);
            return;
        }

        var tenant = await directory.FindByHostAsync(host, context.RequestAborted);
        if (tenant is not null)
        {
            context.SetTenant(tenant);
            tenantContext.Use(tenant.Id);
        }
        else if (RequiresTenant(context.Request.Path))
        {
            await ApiResults.NotFound("unknown_tenant").ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    // Webhooks arrive on the platform host (and must never depend on tenant resolution); the SPA and
    // health endpoints render without a tenant, and the SPA shows "unknown site" when /api/v1/tenant 404s.
    private static bool RequiresTenant(PathString path) =>
        path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/v1/webhooks");
}
