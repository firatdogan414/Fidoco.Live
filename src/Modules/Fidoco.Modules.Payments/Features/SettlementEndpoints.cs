using System.Globalization;
using System.Text;
using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Payments.Features;

/// <summary>
/// Settlement between Fidoco (seller) and the organizer: per event, revenue excluding VAT minus Fidoco's
/// commission is the organizer's share, which the organizer invoices Fidoco for. Refunded purchases count
/// for nobody. Stripe fees are Fidoco's cost (covered by the commission).
/// </summary>
internal static class SettlementEndpoints
{
    /// <summary>VAT the organizer adds to its invoice to Fidoco when VAT-registered (standard Swedish rate).</summary>
    public const decimal OrganizerVatRatePercent = 25m;

    public sealed record Settlement(
        Guid EventId,
        string EventTitle,
        int TicketsSold,
        int TicketsRefunded,
        long GrossAmount,
        long VatAmount,
        long NetAmount,
        long PlatformFee,
        long OrganizerShare,
        long OrganizerVat,
        long OrganizerInvoiceTotal,
        string Currency,
        bool OrganizerVatRegistered);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/admin/events/{eventId:guid}/settlement", ForEvent).RequireAuthorization(AuthPolicies.TenantAdmin);

    private static async Task<IResult> ForEvent(
        Guid eventId,
        string? format,
        PaymentsDbContext db,
        ICatalogQueries catalog,
        ITenantDirectory tenants,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var ev = await catalog.GetEventSummaryAsync(eventId, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        var terms = await tenants.GetCommercialTermsAsync(tenant.RequiredTenantId, cancellationToken);
        var lines = await LinesAsync(db, eventId, cancellationToken);
        var settlement = Calculate(ev, lines, terms.OrganizerVatRegistered);

        return format == "csv"
            ? Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ToCsv(settlement, lines))).ToArray(),
                "text/csv; charset=utf-8", $"avrakning-{ev.Slug}.csv")
            : Results.Ok(settlement);
    }

    internal sealed record SettlementLine(Guid PurchaseId, DateTimeOffset CompletedAt, PurchaseStatus Status, int Quantity, long Gross, long Vat, long Net, long Fee, string Currency);

    internal static async Task<List<SettlementLine>> LinesAsync(PaymentsDbContext db, Guid eventId, CancellationToken cancellationToken) =>
        (await db.PurchaseLines.AsNoTracking()
            .Where(l => l.EventId == eventId)
            .Join(db.Purchases, l => l.PurchaseId, p => p.Id, (l, p) => new SettlementLine(
                p.Id, p.CompletedAt, p.Status, l.Quantity, l.UnitPrice * l.Quantity, l.UnitPrice * l.Quantity - l.NetAmount, l.NetAmount, l.PlatformFeeAmount, p.Currency))
            .ToListAsync(cancellationToken))
        .OrderBy(l => l.CompletedAt)
        .ToList();

    internal static Settlement Calculate(EventSummary ev, IReadOnlyList<SettlementLine> lines, bool organizerVatRegistered)
    {
        var counted = lines.Where(l => l.Status == PurchaseStatus.Completed).ToList();
        var net = counted.Sum(l => l.Net);
        var fee = counted.Sum(l => l.Fee);
        var share = net - fee;
        var organizerVat = organizerVatRegistered ? (long)Math.Round(share * OrganizerVatRatePercent / 100m, MidpointRounding.AwayFromZero) : 0;
        return new Settlement(
            ev.EventId,
            ev.Title,
            counted.Sum(l => l.Quantity),
            lines.Where(l => l.Status == PurchaseStatus.Refunded).Sum(l => l.Quantity),
            counted.Sum(l => l.Gross),
            counted.Sum(l => l.Vat),
            net,
            fee,
            share,
            organizerVat,
            share + organizerVat,
            lines.FirstOrDefault()?.Currency ?? "SEK",
            organizerVatRegistered);
    }

    private static string ToCsv(Settlement s, IReadOnlyList<SettlementLine> lines)
    {
        static string M(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var csv = new StringBuilder();
        csv.AppendLine("Ordernummer;Datum;Status;Antal;Brutto inkl moms;Moms;Netto exkl moms;Fidocos provision;Arrangörens andel exkl moms");
        foreach (var l in lines)
        {
            var counted = l.Status == PurchaseStatus.Completed;
            csv.AppendLine(string.Join(';',
                l.PurchaseId.ToString("N")[..12].ToUpperInvariant(),
                l.CompletedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                counted ? "Genomfört" : "Återbetalt",
                l.Quantity,
                M(l.Gross), M(l.Vat), M(l.Net), M(counted ? l.Fee : 0), M(counted ? l.Net - l.Fee : 0)));
        }

        csv.AppendLine();
        csv.AppendLine($"Summa;;;{s.TicketsSold};{M(s.GrossAmount)};{M(s.VatAmount)};{M(s.NetAmount)};{M(s.PlatformFee)};{M(s.OrganizerShare)}");
        csv.AppendLine($"Arrangörens moms ({(s.OrganizerVatRegistered ? "25 %" : "ej momsregistrerad")});;;;;;;;{M(s.OrganizerVat)}");
        csv.AppendLine($"Att fakturera Fidoco;;;;;;;;{M(s.OrganizerInvoiceTotal)}");
        return csv.ToString();
    }
}
