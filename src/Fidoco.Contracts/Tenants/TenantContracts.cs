using Microsoft.AspNetCore.Http;

namespace Fidoco.Contracts.Tenants;

/// <summary>Public view of a tenant (an event organizer) used by other modules.</summary>
public sealed record TenantInfo(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string PrimaryColor,
    string AccentColor,
    string? SupportEmail,
    string PrimaryHost);

/// <summary>
/// The organizer's commercial terms with Fidoco (merchant of record): Fidoco keeps a commission of the
/// revenue excluding VAT; the organizer invoices Fidoco for the rest.
/// </summary>
public sealed record CommercialTerms(decimal CommissionPercent, long CommissionFixedAmountPerTicket, bool OrganizerVatRegistered);

/// <summary>Looks up tenants. Results are cached briefly; safe to call per request.</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken);

    Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<CommercialTerms> GetCommercialTermsAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Request-level tenant information set by the tenant resolution middleware.</summary>
public static class TenantHttpContextExtensions
{
    private const string TenantKey = "fidoco.tenant";
    private const string PlatformKey = "fidoco.platform-host";

    public static void SetTenant(this HttpContext context, TenantInfo tenant) => context.Items[TenantKey] = tenant;

    public static TenantInfo? GetTenant(this HttpContext context) => context.Items[TenantKey] as TenantInfo;

    public static void MarkPlatformHost(this HttpContext context) => context.Items[PlatformKey] = true;

    /// <summary>True on the platform host (e.g. admin.fidoco.se): no tenant, used for platform admin and webhooks.</summary>
    public static bool IsPlatformHost(this HttpContext context) => context.Items.ContainsKey(PlatformKey);
}
