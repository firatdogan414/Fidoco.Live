using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Playback;
using Fidoco.Modules.Support.Domain;
using Fidoco.Modules.Support.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Fidoco.Modules.Support.Features;

/// <summary>
/// Complimentary tickets (friplatser): access to an event for an e-mail address without payment. Kept apart
/// from sales, counted as 0 kr in the settlement, and revocable. The recipient gets a sign-in link by e-mail.
/// </summary>
internal static class CompEndpoints
{
    public const int MaxNoteLength = 200;

    public sealed record GrantCompRequest(string? Email, Guid TicketTypeId, string? Note);

    public sealed record CompDto(
        Guid EntitlementId, Guid CustomerId, string? Email, Guid TicketTypeId, string? Note, string? GrantedBy, DateTimeOffset GrantedAt,
        bool Revoked, DateTimeOffset? RevokedAt);

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapGet("/events/{eventId:guid}/comps", List);
        admin.MapPost("/events/{eventId:guid}/comps", Grant);
        admin.MapPost("/comps/{entitlementId:guid}/revoke", Revoke);
    }

    private static async Task<IResult> List(Guid eventId, ICatalogQueries catalog, IComplimentaryTickets comps, ICustomerAccounts customers, CancellationToken cancellationToken)
    {
        if (await catalog.GetEventSummaryAsync(eventId, cancellationToken) is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        var list = await comps.ListForEventAsync(eventId, cancellationToken);
        var emails = (await customers.GetManyAsync(list.Select(c => c.CustomerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id, c => c.Email);
        return Results.Ok(list.Select(c => ToDto(c, emails.GetValueOrDefault(c.CustomerId))));
    }

    private static async Task<IResult> Grant(
        Guid eventId,
        GrantCompRequest request,
        HttpContext context,
        ICatalogQueries catalog,
        IComplimentaryTickets comps,
        ICustomerAccounts customers,
        SupportLog log,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Email("email", request.Email).MaxLength("note", request.Note, MaxNoteLength);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var ticket = await catalog.GetPurchasableTicketAsync(request.TicketTypeId, cancellationToken);
        if (ticket is null || ticket.EventId != eventId)
        {
            return ApiResults.NotFound("ticket_not_found");
        }

        // The recipient is e-mailed a link to the event page, which does not exist for viewers until published.
        if (ticket.NotOnSaleReason == "event_not_published")
        {
            return ApiResults.Conflict("event_not_published");
        }

        var email = Emails.Normalize(request.Email!);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var adminEmail = context.User.FindFirst(FidocoClaims.Email)?.Value ?? context.User.GetRequiredUserId().ToString();
        var customerId = await customers.GetOrCreateAsync(email, cancellationToken);
        var result = await comps.GrantAsync(
            new CompGrant(customerId, email, ticket.EventId, ticket.EventTitle, ticket.TicketTypeId, ticket.TicketName,
                ticket.IncludesLive, ticket.ReplayAccessDays, note, adminEmail),
            cancellationToken);
        if (result.Comp is null)
        {
            return ApiResults.Conflict(result.FailureCode ?? "already_has_access");
        }

        var details = note is null ? ticket.TicketName : $"{ticket.TicketName}: {note}";
        await log.RecordAsync(context.User, SupportAction.CompGranted, customerId, email, ticket.EventId, ticket.EventTitle, details, cancellationToken);
        return Results.Ok(ToDto(result.Comp, email));
    }

    private static async Task<IResult> Revoke(
        Guid entitlementId,
        HttpContext context,
        IComplimentaryTickets comps,
        ICustomerAccounts customers,
        ICatalogQueries catalog,
        SupportLog log,
        CancellationToken cancellationToken)
    {
        var comp = await comps.RevokeAsync(entitlementId, cancellationToken);
        if (comp is null)
        {
            return ApiResults.NotFound("comp_not_found");
        }

        var customer = await customers.GetAsync(comp.CustomerId, cancellationToken);
        var ev = await catalog.GetEventSummaryAsync(comp.EventId, cancellationToken);
        await log.RecordAsync(context.User, SupportAction.CompRevoked, comp.CustomerId, customer?.Email, comp.EventId, ev?.Title, comp.Note, cancellationToken);
        return Results.Ok(ToDto(comp, customer?.Email));
    }

    private static CompDto ToDto(CompTicket c, string? email) =>
        new(c.EntitlementId, c.CustomerId, email, c.TicketTypeId, c.Note, c.GrantedBy, c.GrantedAt, c.Revoked, c.RevokedAt);
}
