using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Payments;
using Fidoco.Contracts.Playback;
using Fidoco.Modules.Support.Domain;
using Fidoco.Modules.Support.Infrastructure;
using Fidoco.Modules.Support.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fidoco.Modules.Support.Features;

/// <summary>
/// "I paid but cannot get in": find the buyer by e-mail, see purchases, tickets and whether a device is
/// watching right now, and send a new login link. Every action is written to the support log.
/// </summary>
internal static class CustomerSupportEndpoints
{
    public const int MinimumQueryLength = 3;
    public const int MaxSearchResults = 20;

    public sealed record CustomerDto(Guid Id, string Email, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

    public sealed record PurchaseDto(
        Guid PurchaseId, string OrderNumber, Guid EventId, string EventTitle, string TicketTypeName, int Quantity, long AmountPaid,
        string Currency, DateTimeOffset CompletedAt, bool Refunded);

    public sealed record TicketDto(
        Guid EntitlementId, Guid EventId, string EventTitle, string? EventSlug, DateTimeOffset? EventStartsAt, string Source, bool Revoked,
        bool IncludesLive, int? ReplayAccessDays, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt, string? AccessMode, string? DenyReason,
        bool SessionActive);

    public sealed record LogEntryDto(
        Guid Id, DateTimeOffset CreatedAt, string AdminEmail, SupportAction Action, Guid? CustomerId, string? CustomerEmail,
        Guid? EventId, string? EventTitle, string? Details);

    public sealed record CustomerDetails(CustomerDto Customer, IReadOnlyList<PurchaseDto> Purchases, IReadOnlyList<TicketDto> Tickets, IReadOnlyList<LogEntryDto> Log);

    public sealed record LoginLinkRequest(Guid? EventId);

    public static void Map(IEndpointRouteBuilder api)
    {
        var support = api.MapGroup("/admin/support").RequireAuthorization(AuthPolicies.TenantAdmin);
        support.MapGet("/customers", Search);
        support.MapGet("/customers/{customerId:guid}", Details);
        support.MapPost("/customers/{customerId:guid}/login-link", SendLoginLink);
        support.MapGet("/log", Log);
    }

    /// <summary>Customers whose e-mail contains the query (at least 3 characters), exact match first.</summary>
    private static async Task<IResult> Search(string? email, ICustomerAccounts customers, CancellationToken cancellationToken)
    {
        var query = email?.Trim() ?? "";
        if (query.Length < MinimumQueryLength)
        {
            return new ValidationErrors().Add("email", query.Length == 0 ? "required" : "too_short").ToResult();
        }

        var found = await customers.SearchAsync(query, MaxSearchResults, cancellationToken);
        return Results.Ok(found.Select(c => new CustomerDto(c.Id, c.Email, c.CreatedAt, c.LastLoginAt)));
    }

    private static async Task<IResult> Details(
        Guid customerId,
        HttpContext context,
        ICustomerAccounts customers,
        IPurchaseQueries purchases,
        ICustomerTickets tickets,
        ICatalogQueries catalog,
        SupportDbContext db,
        SupportLog log,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetProfileAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return ApiResults.NotFound("customer_not_found");
        }

        await log.RecordAsync(context.User, SupportAction.CustomerViewed, customer.Id, customer.Email, cancellationToken: cancellationToken);

        var bought = await purchases.GetForCustomerAsync(customerId, cancellationToken);
        var access = await tickets.GetForCustomerAsync(customerId, cancellationToken);
        var events = new Dictionary<Guid, EventSummary?>();
        foreach (var eventId in access.Select(a => a.EventId).Distinct())
        {
            events[eventId] = await catalog.GetEventSummaryAsync(eventId, cancellationToken);
        }

        var entries = await db.Log.AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return Results.Ok(new CustomerDetails(
            new CustomerDto(customer.Id, customer.Email, customer.CreatedAt, customer.LastLoginAt),
            bought.Select(p => new PurchaseDto(
                p.PurchaseId, p.OrderNumber, p.EventId, p.EventTitle, p.TicketTypeName, p.Quantity, p.AmountPaid, p.Currency, p.CompletedAt, p.Refunded)).ToList(),
            access.Select(a =>
            {
                var ev = events[a.EventId];
                return new TicketDto(
                    a.EntitlementId, a.EventId, ev?.Title ?? "", ev?.Slug, ev?.StartsAt, a.SourceType.ToLowerInvariant(), a.Revoked, a.IncludesLive,
                    a.ReplayAccessDays, a.CreatedAt, a.RevokedAt, a.AccessMode, a.DenyReason, a.SessionActive);
            }).ToList(),
            entries.Select(ToDto).ToList()));
    }

    /// <summary>A new single-use login link (24 hours) to the customer's own address; to the watch page when an event is given.</summary>
    private static async Task<IResult> SendLoginLink(
        Guid customerId,
        LoginLinkRequest request,
        HttpContext context,
        ICustomerAccounts customers,
        ICustomerLoginLinks links,
        ICatalogQueries catalog,
        SupportLog log,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return ApiResults.NotFound("customer_not_found");
        }

        EventSummary? ev = null;
        if (request.EventId is { } eventId)
        {
            ev = await catalog.GetEventSummaryAsync(eventId, cancellationToken);
            if (ev is null)
            {
                return ApiResults.NotFound("event_not_found");
            }
        }

        try
        {
            await links.SendSupportLinkAsync(customer.Id, ev is null ? "/" : $"/e/{ev.Slug}/titta", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggers.CreateLogger(typeof(CustomerSupportEndpoints)).LogError(ex, "Sending support login link failed");
            return ApiResults.Problem(StatusCodes.Status503ServiceUnavailable, "email_unavailable");
        }

        await log.RecordAsync(context.User, SupportAction.LoginLinkSent, customer.Id, customer.Email, ev?.EventId, ev?.Title, cancellationToken: cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> Log(Guid? customerId, Guid? eventId, int? limit, SupportDbContext db, CancellationToken cancellationToken)
    {
        var query = db.Log.AsNoTracking();
        if (customerId is not null)
        {
            query = query.Where(e => e.CustomerId == customerId);
        }

        if (eventId is not null)
        {
            query = query.Where(e => e.EventId == eventId);
        }

        var entries = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(limit ?? 100, 1, 500))
            .ToListAsync(cancellationToken);
        return Results.Ok(entries.Select(ToDto));
    }

    internal static LogEntryDto ToDto(SupportLogEntry e) =>
        new(e.Id, e.CreatedAt, e.AdminEmail, e.Action, e.CustomerId, e.CustomerEmail, e.EventId, e.EventTitle, e.Details);
}
