using Fidoco.Contracts.Playback;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Playback.Domain;

internal enum EntitlementStatus
{
    Active,
    Revoked,
}

/// <summary>
/// The right to watch an event. Created from a purchase or as a complimentary ticket (comp) by an admin;
/// free registrations, access codes or physical tickets with a stream can create entitlements later
/// without touching Payments.
/// </summary>
internal sealed class Entitlement : AggregateRoot, ITenantOwned
{
    public const string PurchaseSource = EntitlementSources.Purchase;
    public const string CompSource = EntitlementSources.Comp;

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid EventId { get; set; }

    public Guid TicketTypeId { get; set; }

    public required string SourceType { get; set; }

    /// <summary>
    /// For purchases: the purchase line id. Unique, which makes granting idempotent. For comps: the
    /// entitlement's own id (each grant is its own source).
    /// </summary>
    public Guid SourceId { get; set; }

    public bool IncludesLive { get; set; }

    public int? ReplayAccessDays { get; set; }

    public EntitlementStatus Status { get; set; } = EntitlementStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Comps: why it was given (e.g. "Press"), shown in the admin.</summary>
    public string? Note { get; set; }

    /// <summary>Comps: the admin who granted it.</summary>
    public string? GrantedBy { get; set; }

    public static Entitlement GrantComp(CompGrant grant, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7(now);
        var entitlement = new Entitlement
        {
            Id = id,
            CustomerId = grant.CustomerId,
            EventId = grant.EventId,
            TicketTypeId = grant.TicketTypeId,
            SourceType = CompSource,
            SourceId = id,
            IncludesLive = grant.IncludesLive,
            ReplayAccessDays = grant.ReplayAccessDays,
            Note = grant.Note,
            GrantedBy = grant.GrantedBy,
            CreatedAt = now,
        };
        entitlement.Raise(new ComplimentaryTicketGranted(
            id, grant.CustomerId, grant.Email, grant.EventId, grant.EventTitle, grant.TicketTypeName, grant.ReplayAccessDays));
        return entitlement;
    }

    public bool Revoke(DateTimeOffset now)
    {
        if (Status == EntitlementStatus.Revoked)
        {
            return false;
        }

        Status = EntitlementStatus.Revoked;
        RevokedAt = now;
        return true;
    }
}
