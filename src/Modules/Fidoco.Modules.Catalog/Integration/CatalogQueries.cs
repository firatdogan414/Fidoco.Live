using Fidoco.Contracts.Catalog;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fidoco.Modules.Catalog.Integration;

internal sealed class CatalogQueries(CatalogDbContext db, ITenantContext tenant, IMemoryCache cache, TimeProvider time) : ICatalogQueries
{
    public static readonly TimeSpan PlaybackInfoCacheDuration = TimeSpan.FromSeconds(5);

    public async Task<PurchasableTicket?> GetPurchasableTicketAsync(Guid ticketTypeId, CancellationToken cancellationToken)
    {
        var ticket = await db.TicketTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketTypeId, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == ticket.EventId, cancellationToken);
        return new PurchasableTicket(
            ticket.Id, ev.Id, ev.Slug, ev.Title, ticket.Name, ticket.PriceAmount, ticket.Currency, ticket.VatRatePercent,
            ticket.IncludesLive, ticket.ReplayAccessDays, TicketSaleRules.NotOnSaleReason(ev, ticket, time.GetUtcNow()));
    }

    public Task<EventPlaybackInfo?> GetPlaybackInfoAsync(Guid eventId, CancellationToken cancellationToken) =>
        // The tenant is part of the key so a cached entry can never be served to another tenant.
        cache.GetOrCreateAsync($"catalog:playback:{tenant.TenantId}:{eventId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlaybackInfoCacheDuration;
            var ev = await db.Events.AsNoTracking()
                .Include(e => e.Streams).ThenInclude(s => s.Recordings)
                .AsSplitQuery()
                .SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (ev is null)
            {
                return null;
            }

            var stream = ev.PrimaryStream;
            return new EventPlaybackInfo(ev.Id, ev.Status, ev.StartsAt, ev.EndedAt, stream?.LivePlaybackId,
                stream?.Status == StreamStatus.Active, ev.ReplayPlaybackId);
        });

    public Task<EventSummary?> GetEventSummaryAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.Events.AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new EventSummary(e.Id, e.Slug, e.Title, e.StartsAt))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<EventShareInfo?> GetShareInfoAsync(string slug, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync($"catalog:share:{tenant.TenantId}:{slug}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return db.Events.AsNoTracking()
                .Where(e => e.Slug == slug && e.Status != EventStatus.Draft)
                .Select(e => new EventShareInfo(e.Slug, e.Title, e.Description, e.ImageUrl, e.StartsAt))
                .SingleOrDefaultAsync(cancellationToken);
        });
}

/// <summary>
/// Phase 1: tickets have no capacity, so every reservation succeeds without being stored. When physical
/// tickets get a <see cref="TicketType.Capacity"/>, this is where a reservation table and the capacity
/// check go (the checkout flow already calls reserve/confirm/release).
/// </summary>
internal sealed class TicketAvailability(CatalogDbContext db) : ITicketAvailability
{
    public async Task<ReservationResult> TryReserveAsync(Guid ticketTypeId, int quantity, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var capacity = await db.TicketTypes.Where(t => t.Id == ticketTypeId).Select(t => t.Capacity).SingleOrDefaultAsync(cancellationToken);
        return capacity is null
            ? ReservationResult.Unlimited
            : new ReservationResult(false, null, "capacity_not_supported");
    }

    public Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) => Task.CompletedTask;
}
