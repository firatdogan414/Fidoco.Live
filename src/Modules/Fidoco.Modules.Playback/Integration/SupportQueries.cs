using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Playback;
using Fidoco.Modules.Playback.Domain;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.Modules.Playback.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Playback.Integration;

/// <summary>A customer's tickets with what they allow right now, for the organizer's support.</summary>
internal sealed class CustomerTickets(PlaybackDbContext db, ICatalogQueries catalog, PlaybackSessionStore sessions, TimeProvider time) : ICustomerTickets
{
    public async Task<IReadOnlyList<CustomerTicketAccess>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var entitlements = await db.Entitlements.AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        var result = new List<CustomerTicketAccess>();
        foreach (var e in entitlements)
        {
            var info = await catalog.GetPlaybackInfoAsync(e.EventId, cancellationToken);
            var decision = info is null ? AccessDecision.Deny("event_not_found") : AccessPolicy.Evaluate(e, info, now);
            var sessionActive = e.Status == EntitlementStatus.Active && await sessions.GetActiveSessionIdAsync(e.Id, cancellationToken) is not null;
            result.Add(new CustomerTicketAccess(
                e.Id, e.EventId, e.TicketTypeId, e.SourceType, e.SourceId, e.Status == EntitlementStatus.Revoked, e.IncludesLive,
                e.ReplayAccessDays, e.CreatedAt, e.RevokedAt, decision.Mode?.ToString().ToLowerInvariant(), decision.DenyReason, sessionActive));
        }

        return result;
    }
}

/// <summary>Complimentary tickets (friplatser): entitlements with source "Comp", granted and revoked by admins.</summary>
internal sealed class ComplimentaryTickets(PlaybackDbContext db, PlaybackSessionStore sessions, TimeProvider time) : IComplimentaryTickets
{
    public async Task<CompGrantResult> GrantAsync(CompGrant grant, CancellationToken cancellationToken)
    {
        var hasAccess = await db.Entitlements.AnyAsync(
            e => e.CustomerId == grant.CustomerId && e.EventId == grant.EventId && e.Status == EntitlementStatus.Active, cancellationToken);
        if (hasAccess)
        {
            return new CompGrantResult(null, "already_has_access");
        }

        var entitlement = Entitlement.GrantComp(grant, time.GetUtcNow());
        db.Entitlements.Add(entitlement);
        await db.SaveChangesAsync(cancellationToken);
        return new CompGrantResult(ToComp(entitlement), null);
    }

    public async Task<CompTicket?> RevokeAsync(Guid entitlementId, CancellationToken cancellationToken)
    {
        var entitlement = await db.Entitlements.SingleOrDefaultAsync(
            e => e.Id == entitlementId && e.SourceType == Entitlement.CompSource, cancellationToken);
        if (entitlement is null)
        {
            return null;
        }

        if (entitlement.Revoke(time.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // A device watching on the comp stops at its next heartbeat.
        await sessions.MarkRevokedAsync(entitlement.Id, cancellationToken);
        await sessions.ClearActiveAsync(entitlement.Id, cancellationToken);
        return ToComp(entitlement);
    }

    public async Task<IReadOnlyList<CompTicket>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var comps = await db.Entitlements.AsNoTracking()
            .Where(e => e.EventId == eventId && e.SourceType == Entitlement.CompSource)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        return comps.Select(ToComp).ToList();
    }

    public Task<int> CountActiveForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.Entitlements.CountAsync(
            e => e.EventId == eventId && e.SourceType == Entitlement.CompSource && e.Status == EntitlementStatus.Active, cancellationToken);

    private static CompTicket ToComp(Entitlement e) =>
        new(e.Id, e.CustomerId, e.EventId, e.TicketTypeId, e.Note, e.GrantedBy, e.CreatedAt, e.Status == EntitlementStatus.Revoked, e.RevokedAt);
}
