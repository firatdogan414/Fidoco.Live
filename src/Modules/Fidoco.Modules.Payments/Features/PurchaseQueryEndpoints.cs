using Fidoco.Contracts.Identity;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Payments.Features;

/// <summary>The buyer's own purchases and the organizer's sales figures.</summary>
internal static class PurchaseQueryEndpoints
{
    public sealed record MyPurchase(Guid PurchaseId, Guid EventId, string EventTitle, string TicketTypeName, long Amount, string Currency, DateTimeOffset CompletedAt, string Status);

    public sealed record EventSales(Guid EventId, int TicketsSold, int TicketsRefunded, long Revenue, string Currency);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/me/purchases", MyPurchases).RequireAuthorization(AuthPolicies.Customer);
        api.MapGet("/admin/sales", AllSales).RequireAuthorization(AuthPolicies.TenantAdmin);
        api.MapGet("/admin/events/{eventId:guid}/sales", SalesForEvent).RequireAuthorization(AuthPolicies.TenantAdmin);
    }

    private static async Task<IResult> MyPurchases(HttpContext context, PaymentsDbContext db, CancellationToken cancellationToken)
    {
        var customerId = context.User.GetRequiredUserId();
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .SelectMany(p => p.Lines, (p, l) => new MyPurchase(
                p.Id, l.EventId, l.EventTitle, l.TicketTypeName, l.UnitPrice * l.Quantity, p.Currency, p.CompletedAt,
                p.Status == PurchaseStatus.Refunded ? "refunded" : "completed"))
            .ToListAsync(cancellationToken);
        return Results.Ok(purchases.OrderByDescending(p => p.CompletedAt));
    }

    private static async Task<IResult> AllSales(PaymentsDbContext db, CancellationToken cancellationToken) =>
        Results.Ok(await SalesQuery(db, null, cancellationToken));

    private static async Task<IResult> SalesForEvent(Guid eventId, PaymentsDbContext db, CancellationToken cancellationToken)
    {
        var sales = await SalesQuery(db, eventId, cancellationToken);
        return Results.Ok(sales.SingleOrDefault() ?? new EventSales(eventId, 0, 0, 0, "SEK"));
    }

    /// <summary>Gross revenue including VAT of completed (not refunded) purchases.</summary>
    private static async Task<List<EventSales>> SalesQuery(PaymentsDbContext db, Guid? eventId, CancellationToken cancellationToken)
    {
        var lines = await db.PurchaseLines.AsNoTracking()
            .Where(l => eventId == null || l.EventId == eventId)
            .Join(db.Purchases, l => l.PurchaseId, p => p.Id, (l, p) => new { l.EventId, l.Quantity, l.UnitPrice, p.Status, p.Currency })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(l => l.EventId)
            .Select(g => new EventSales(
                g.Key,
                g.Where(l => l.Status == PurchaseStatus.Completed).Sum(l => l.Quantity),
                g.Where(l => l.Status == PurchaseStatus.Refunded).Sum(l => l.Quantity),
                g.Where(l => l.Status == PurchaseStatus.Completed).Sum(l => l.UnitPrice * l.Quantity),
                g.First().Currency))
            .ToList();
    }
}
