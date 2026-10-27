using Fidoco.SharedKernel.Domain;

namespace Fidoco.Contracts.Playback;

/// <summary>Where an entitlement (the right to watch an event) comes from.</summary>
public static class EntitlementSources
{
    /// <summary>A paid purchase line (SourceId = purchase line id).</summary>
    public const string Purchase = "Purchase";

    /// <summary>A complimentary ticket (friplats) granted by an admin without payment; counts as 0 kr.</summary>
    public const string Comp = "Comp";
}

/// <summary>
/// One of a customer's tickets and what it allows right now. <see cref="AccessMode"/> is "live" or "replay"
/// when the ticket can be watched at this moment, otherwise <see cref="DenyReason"/> says why not (the same
/// codes the viewer sees). <see cref="SessionActive"/> is true while a device holds the ticket.
/// </summary>
public sealed record CustomerTicketAccess(
    Guid EntitlementId,
    Guid EventId,
    Guid TicketTypeId,
    string SourceType,
    Guid SourceId,
    bool Revoked,
    bool IncludesLive,
    int? ReplayAccessDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    string? AccessMode,
    string? DenyReason,
    bool SessionActive);

/// <summary>Read access to customers' tickets for support (current tenant).</summary>
public interface ICustomerTickets
{
    Task<IReadOnlyList<CustomerTicketAccess>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>What a complimentary ticket gives; the access terms are copied from a ticket type of the event.</summary>
public sealed record CompGrant(
    Guid CustomerId,
    string Email,
    Guid EventId,
    string EventTitle,
    Guid TicketTypeId,
    string TicketTypeName,
    bool IncludesLive,
    int? ReplayAccessDays,
    string? Note,
    string GrantedBy);

public sealed record CompTicket(
    Guid EntitlementId,
    Guid CustomerId,
    Guid EventId,
    Guid TicketTypeId,
    string? Note,
    string? GrantedBy,
    DateTimeOffset GrantedAt,
    bool Revoked,
    DateTimeOffset? RevokedAt);

/// <summary><see cref="FailureCode"/> is "already_has_access" when the customer can already watch the event.</summary>
public sealed record CompGrantResult(CompTicket? Comp, string? FailureCode);

/// <summary>Complimentary tickets (friplatser): access without payment, kept apart from sales (current tenant).</summary>
public interface IComplimentaryTickets
{
    Task<CompGrantResult> GrantAsync(CompGrant grant, CancellationToken cancellationToken);

    /// <summary>Revokes a comp and stops a running session. Null when there is no such comp; idempotent.</summary>
    Task<CompTicket?> RevokeAsync(Guid entitlementId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompTicket>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Comps that are not revoked; the settlement shows them at 0 kr.</summary>
    Task<int> CountActiveForEventAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>A complimentary ticket was granted; Notifications e-mails the recipient a sign-in link.</summary>
public sealed record ComplimentaryTicketGranted(
    Guid EntitlementId,
    Guid CustomerId,
    string Email,
    Guid EventId,
    string EventTitle,
    string TicketTypeName,
    int? ReplayAccessDays) : IDomainEvent;
