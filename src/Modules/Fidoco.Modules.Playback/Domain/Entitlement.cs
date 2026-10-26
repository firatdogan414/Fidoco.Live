using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Playback.Domain;

internal enum EntitlementStatus
{
    Active,
    Revoked,
}

/// <summary>
/// The right to watch an event. Created from a purchase today; free registrations, access codes or
/// physical tickets with a stream can create entitlements later without touching Payments.
/// </summary>
internal sealed class Entitlement : ITenantOwned
{
    public const string PurchaseSource = "Purchase";

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid EventId { get; set; }

    public Guid TicketTypeId { get; set; }

    public required string SourceType { get; set; }

    /// <summary>For purchases: the purchase line id. Unique, which makes granting idempotent.</summary>
    public Guid SourceId { get; set; }

    public bool IncludesLive { get; set; }

    public int? ReplayAccessDays { get; set; }

    public EntitlementStatus Status { get; set; } = EntitlementStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
