using Fidoco.Contracts.Payments;
using Fidoco.Modules.Playback.Domain;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.Modules.Playback.Sessions;
using Fidoco.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Playback.Integration;

/// <summary>Grants access after payment (via the outbox). Idempotent: one entitlement per purchase line.</summary>
internal sealed class GrantEntitlementsOnPurchase(PlaybackDbContext db, TimeProvider time) : IDomainEventHandler<PurchaseCompleted>
{
    public async Task HandleAsync(PurchaseCompleted purchase, CancellationToken cancellationToken)
    {
        var lineIds = purchase.Lines.Select(l => l.PurchaseLineId).ToList();
        var existing = await db.Entitlements
            .Where(e => e.SourceType == Entitlement.PurchaseSource && lineIds.Contains(e.SourceId))
            .Select(e => e.SourceId)
            .ToListAsync(cancellationToken);

        foreach (var line in purchase.Lines.Where(l => !existing.Contains(l.PurchaseLineId)))
        {
            db.Entitlements.Add(new Entitlement
            {
                Id = Guid.CreateVersion7(),
                CustomerId = purchase.CustomerId,
                EventId = line.EventId,
                TicketTypeId = line.TicketTypeId,
                SourceType = Entitlement.PurchaseSource,
                SourceId = line.PurchaseLineId,
                IncludesLive = line.IncludesLive,
                ReplayAccessDays = line.ReplayAccessDays,
                CreatedAt = time.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Revokes access after a refund and stops any running session at its next heartbeat.</summary>
internal sealed class RevokeEntitlementsOnRefund(PlaybackDbContext db, PlaybackSessionStore sessions, TimeProvider time) : IDomainEventHandler<PurchaseRefunded>
{
    public async Task HandleAsync(PurchaseRefunded refund, CancellationToken cancellationToken)
    {
        var lineIds = refund.PurchaseLineIds.ToList();
        var entitlements = await db.Entitlements
            .Where(e => e.SourceType == Entitlement.PurchaseSource && lineIds.Contains(e.SourceId) && e.Status == EntitlementStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var entitlement in entitlements)
        {
            entitlement.Status = EntitlementStatus.Revoked;
            entitlement.RevokedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var entitlement in entitlements)
        {
            await sessions.MarkRevokedAsync(entitlement.Id, cancellationToken);
            await sessions.ClearActiveAsync(entitlement.Id, cancellationToken);
        }
    }
}

/// <summary>GDPR: a deleted customer's entitlements are removed and running sessions stop.</summary>
internal sealed class DeleteEntitlementsOnCustomerDeleted(PlaybackDbContext db, PlaybackSessionStore sessions) : IDomainEventHandler<Fidoco.Contracts.Identity.CustomerDeleted>
{
    public async Task HandleAsync(Fidoco.Contracts.Identity.CustomerDeleted domainEvent, CancellationToken cancellationToken)
    {
        var entitlements = await db.Entitlements.Where(e => e.CustomerId == domainEvent.CustomerId).ToListAsync(cancellationToken);
        db.Entitlements.RemoveRange(entitlements);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var entitlement in entitlements)
        {
            await sessions.MarkRevokedAsync(entitlement.Id, cancellationToken);
            await sessions.ClearActiveAsync(entitlement.Id, cancellationToken);
        }
    }
}
