using Fidoco.Contracts.Catalog;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fidoco.Modules.Catalog.Features.Public;

/// <summary>Public event pages for viewers. Drafts are never visible.</summary>
internal static class PublicEventEndpoints
{
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(5);

    public sealed record EventListItem(Guid Id, string Slug, string Title, string? ImageUrl, DateTimeOffset StartsAt, EventStatus Status);

    public sealed record TicketTypeDto(Guid Id, string Name, long PriceAmount, string Currency, bool IncludesLive, int? ReplayAccessDays, bool OnSale, string? NotOnSaleReason);

    public sealed record EventDetail(
        Guid Id, string Slug, string Title, string? Description, string? ImageUrl, DateTimeOffset StartsAt,
        EventStatus Status, DateTimeOffset? EndedAt, IReadOnlyList<TicketTypeDto> TicketTypes);

    public sealed record EventStatusDto(Guid Id, EventStatus Status, DateTimeOffset StartsAt, DateTimeOffset? EndedAt, bool StreamActive, bool ReplayAvailable);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/events", ListEvents);
        api.MapGet("/events/{slug}", GetEvent);
        api.MapGet("/events/{slug}/status", GetStatus);
    }

    private static async Task<IResult> ListEvents(CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var archiveFrom = time.GetUtcNow().AddDays(-60);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.Status != EventStatus.Draft && (e.Status != EventStatus.Ended || e.EndedAt >= archiveFrom))
            .OrderBy(e => e.StartsAt)
            .Select(e => new EventListItem(e.Id, e.Slug, e.Title, e.ImageUrl, e.StartsAt, e.Status))
            .ToListAsync(cancellationToken);

        // Live first, then upcoming (soonest first), then ended (most recent first).
        var ordered = events.Where(e => e.Status == EventStatus.Live)
            .Concat(events.Where(e => e.Status == EventStatus.Scheduled))
            .Concat(events.Where(e => e.Status == EventStatus.Ended).OrderByDescending(e => e.StartsAt))
            .ToList();
        return Results.Ok(ordered);
    }

    private static async Task<IResult> GetEvent(string slug, CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ev = await db.Events.AsNoTracking()
            .Include(e => e.TicketTypes)
            .SingleOrDefaultAsync(e => e.Slug == slug && e.Status != EventStatus.Draft, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        var now = time.GetUtcNow();
        var tickets = ev.TicketTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t =>
            {
                var reason = TicketSaleRules.NotOnSaleReason(ev, t, now);
                return new TicketTypeDto(t.Id, t.Name, t.PriceAmount, t.Currency, t.IncludesLive, t.ReplayAccessDays, reason is null, reason);
            })
            .ToList();

        return Results.Ok(new EventDetail(ev.Id, ev.Slug, ev.Title, ev.Description, ev.ImageUrl, ev.StartsAt, ev.Status, ev.EndedAt, tickets));
    }

    /// <summary>Polled by the watch page (every 15 s per viewer), so it is cached briefly per tenant.</summary>
    private static async Task<IResult> GetStatus(string slug, CatalogDbContext db, ITenantContext tenant, IMemoryCache cache, CancellationToken cancellationToken)
    {
        var status = await cache.GetOrCreateAsync($"catalog:status:{tenant.TenantId}:{slug}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StatusCacheDuration;
            var ev = await db.Events.AsNoTracking()
                .Include(e => e.Streams).ThenInclude(s => s.Recordings)
                .AsSplitQuery()
                .SingleOrDefaultAsync(e => e.Slug == slug && e.Status != EventStatus.Draft, cancellationToken);
            return ev is null
                ? null
                : new EventStatusDto(ev.Id, ev.Status, ev.StartsAt, ev.EndedAt, ev.PrimaryStream?.Status == StreamStatus.Active, ev.ReplayPlaybackId is not null);
        });

        return status is null ? ApiResults.NotFound("event_not_found") : Results.Ok(status);
    }
}
