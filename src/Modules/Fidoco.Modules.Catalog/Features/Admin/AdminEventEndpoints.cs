using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Catalog.Features.Admin;

/// <summary>Event management for the organizer (TenantAdmin). Phase 1: one paid digital ticket per event.</summary>
internal static class AdminEventEndpoints
{
    public sealed record TicketInput(string? Name, long PriceAmount, int? ReplayAccessDays);

    public sealed record EventInput(string? Title, string? Slug, string? Description, string? ImageUrl, DateTimeOffset? StartsAt, TicketInput? Ticket);

    public sealed record AdminTicketDto(Guid Id, string Name, long PriceAmount, string Currency, decimal VatRatePercent, bool IncludesLive, int? ReplayAccessDays);

    public sealed record AdminStreamSummary(Guid Id, string Name, string Status);

    public sealed record AdminEventDto(
        Guid Id, string Slug, string Title, string? Description, string? ImageUrl, DateTimeOffset StartsAt, EventStatus Status,
        DateTimeOffset? LiveStartedAt, DateTimeOffset? EndedAt, AdminTicketDto? Ticket, AdminStreamSummary? Stream);

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin/events").RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapGet("/", List);
        admin.MapPost("/", Create);
        admin.MapGet("/{id:guid}", Get);
        admin.MapPut("/{id:guid}", Update);
        admin.MapPost("/{id:guid}/publish", Publish);
        admin.MapPost("/{id:guid}/end", End);
    }

    private static async Task<IResult> List(CatalogDbContext db, CancellationToken cancellationToken)
    {
        var events = await LoadQuery(db).OrderByDescending(e => e.StartsAt).ToListAsync(cancellationToken);
        return Results.Ok(events.Select(ToDto));
    }

    private static async Task<IResult> Get(Guid id, CatalogDbContext db, CancellationToken cancellationToken)
    {
        var ev = await LoadQuery(db).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return ev is null ? ApiResults.NotFound("event_not_found") : Results.Ok(ToDto(ev));
    }

    private static async Task<IResult> Create(EventInput input, CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var slug = string.IsNullOrWhiteSpace(input.Slug) ? await UniqueSlugAsync(db, Slugs.From(input.Title!), null, cancellationToken) : input.Slug.Trim();
        if (await db.Events.AnyAsync(e => e.Slug == slug, cancellationToken))
        {
            return ApiResults.Conflict("slug_taken");
        }

        var now = time.GetUtcNow();
        var ev = new Event
        {
            Id = Guid.CreateVersion7(now),
            Slug = slug,
            Title = input.Title!.Trim(),
            CreatedAt = now,
        };
        Apply(ev, input, now);
        ev.TicketTypes.Add(new TicketType { Id = Guid.CreateVersion7(now), EventId = ev.Id, Name = TicketName(input.Ticket!) });
        ApplyTicket(ev.TicketTypes[0], input.Ticket!);
        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/admin/events/{ev.Id}", ToDto(ev));
    }

    private static async Task<IResult> Update(Guid id, EventInput input, CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var ev = await LoadQuery(db, tracking: true).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        if (!string.IsNullOrWhiteSpace(input.Slug) && input.Slug.Trim() != ev.Slug)
        {
            var slug = input.Slug.Trim();
            if (await db.Events.AnyAsync(e => e.Slug == slug && e.Id != id, cancellationToken))
            {
                return ApiResults.Conflict("slug_taken");
            }

            ev.Slug = slug;
        }

        ev.Title = input.Title!.Trim();
        Apply(ev, input, time.GetUtcNow());
        var ticket = ev.TicketTypes.OrderBy(t => t.SortOrder).First();
        ticket.Name = TicketName(input.Ticket!);
        ApplyTicket(ticket, input.Ticket!);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDto(ev));
    }

    private static async Task<IResult> Publish(Guid id, CatalogDbContext db, CancellationToken cancellationToken)
    {
        var ev = await LoadQuery(db, tracking: true).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        if (!ev.Publish())
        {
            return ApiResults.Conflict("event_not_draft");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDto(ev));
    }

    private static async Task<IResult> End(Guid id, CatalogDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ev = await LoadQuery(db, tracking: true).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        if (!ev.End(time.GetUtcNow()))
        {
            return ApiResults.Conflict("event_not_running");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDto(ev));
    }

    private static IQueryable<Event> LoadQuery(CatalogDbContext db, bool tracking = false)
    {
        var query = db.Events.Include(e => e.TicketTypes).Include(e => e.Streams).AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    private static void Apply(Event ev, EventInput input, DateTimeOffset now)
    {
        ev.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        ev.ImageUrl = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl.Trim();
        ev.StartsAt = input.StartsAt!.Value.ToUniversalTime();
        ev.UpdatedAt = now;
    }

    private static void ApplyTicket(TicketType ticket, TicketInput input)
    {
        ticket.PriceAmount = input.PriceAmount;
        ticket.ReplayAccessDays = input.ReplayAccessDays;
    }

    private static string TicketName(TicketInput input) => string.IsNullOrWhiteSpace(input.Name) ? "Livestream" : input.Name.Trim();

    private static ValidationErrors Validate(EventInput input)
    {
        var errors = new ValidationErrors()
            .Required("title", input.Title, maxLength: 200)
            .MaxLength("description", input.Description, 5000)
            .MaxLength("imageUrl", input.ImageUrl, 500);
        if (input.StartsAt is null)
        {
            errors.Add("startsAt", "required");
        }

        if (!string.IsNullOrWhiteSpace(input.Slug) && !Slugs.IsValid(input.Slug.Trim()))
        {
            errors.Add("slug", "invalid_slug");
        }

        if (!string.IsNullOrWhiteSpace(input.ImageUrl)
            && !input.ImageUrl.StartsWith('/')
            && !(Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
        {
            errors.Add("imageUrl", "invalid_url");
        }

        if (input.Ticket is null)
        {
            errors.Add("ticket", "required");
            return errors;
        }

        errors.MaxLength("ticket.name", input.Ticket.Name, 100);
        if (input.Ticket.PriceAmount < TicketType.MinimumPaidAmount || input.Ticket.PriceAmount > 1_000_000)
        {
            errors.Add("ticket.priceAmount", "out_of_range");
        }

        if (input.Ticket.ReplayAccessDays is < 1 or > 365)
        {
            errors.Add("ticket.replayAccessDays", "out_of_range");
        }

        return errors;
    }

    private static async Task<string> UniqueSlugAsync(CatalogDbContext db, string baseSlug, Guid? exceptId, CancellationToken cancellationToken)
    {
        var taken = await db.Events
            .Where(e => e.Slug.StartsWith(baseSlug) && e.Id != exceptId)
            .Select(e => e.Slug)
            .ToListAsync(cancellationToken);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static AdminEventDto ToDto(Event ev)
    {
        var ticket = ev.TicketTypes.OrderBy(t => t.SortOrder).FirstOrDefault();
        var stream = ev.PrimaryStream;
        return new AdminEventDto(
            ev.Id, ev.Slug, ev.Title, ev.Description, ev.ImageUrl, ev.StartsAt, ev.Status, ev.LiveStartedAt, ev.EndedAt,
            ticket is null ? null : new AdminTicketDto(ticket.Id, ticket.Name, ticket.PriceAmount, ticket.Currency, ticket.VatRatePercent, ticket.IncludesLive, ticket.ReplayAccessDays),
            stream is null ? null : new AdminStreamSummary(stream.Id, stream.Name, stream.Status.ToString().ToLowerInvariant()));
    }
}
