using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Tenants.Domain;
using Fidoco.Modules.Tenants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fidoco.Modules.Tenants.Integration;

/// <summary>
/// Tenant lookups with a short per-instance read cache. This is a cache of platform data, not session
/// state, so it is safe with multiple instances (changes show up within <see cref="CacheDuration"/>).
/// </summary>
internal sealed class TenantDirectory(TenantsDbContext db, IMemoryCache cache) : ITenantDirectory
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken)
    {
        var normalized = NormalizeHost(host);
        return await cache.GetOrCreateAsync($"tenants:host:{normalized}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var tenantId = await db.TenantDomains.AsNoTracking()
                .Where(d => d.Host == normalized)
                .Select(d => (Guid?)d.TenantId)
                .SingleOrDefaultAsync(cancellationToken);
            return tenantId is null ? null : await LoadAsync(tenantId.Value, cancellationToken);
        });
    }

    public Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync($"tenants:id:{tenantId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return LoadAsync(tenantId, cancellationToken);
        });

    public async Task<CommercialTerms> GetCommercialTermsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new CommercialTerms(t.CommissionPercent, t.CommissionFixedAmountPerTicket, t.OrganizerVatRegistered))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

    /// <summary>Drops cached lookups after a change (other instances catch up within the cache duration).</summary>
    public void Invalidate(Guid tenantId, IEnumerable<string> hosts)
    {
        cache.Remove($"tenants:id:{tenantId}");
        foreach (var host in hosts)
        {
            cache.Remove($"tenants:host:{NormalizeHost(host)}");
        }
    }

    public static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();

    private async Task<TenantInfo?> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Include(t => t.Domains)
            .SingleOrDefaultAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var primaryHost = tenant.Domains.OrderByDescending(d => d.IsPrimary).Select(d => d.Host).FirstOrDefault() ?? "";
        return new TenantInfo(tenant.Id, tenant.Name, tenant.Slug, tenant.LogoUrl, tenant.PrimaryColor,
            tenant.AccentColor, tenant.SupportEmail, primaryHost);
    }
}
