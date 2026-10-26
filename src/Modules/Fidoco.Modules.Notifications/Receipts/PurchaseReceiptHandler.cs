using System.Globalization;
using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Notifications;
using Fidoco.Contracts.Payments;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Notifications.Email;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Notifications.Receipts;

/// <summary>
/// Sends the receipt with a magic link after a purchase (via the outbox, with retries). Fidoco is the
/// seller; the tenant is shown as organizer. The login link is created when sending, so no raw token is
/// ever stored in the outbox.
/// </summary>
internal sealed class PurchaseReceiptHandler(
    IEmailDispatcher email,
    EmailTemplateRenderer texts,
    ICustomerLoginLinks loginLinks,
    ICatalogQueries catalog,
    ITenantDirectory tenants,
    ITenantContext tenantContext,
    IOptions<EmailOptions> emailOptions,
    IOptions<SellerOptions> seller,
    TimeProvider time) : IDomainEventHandler<PurchaseCompleted>
{
    /// <summary>The receipt link works until replay access ends, at most 30 days after the event.</summary>
    public static readonly TimeSpan MaxLinkLifetimeAfterEvent = TimeSpan.FromDays(30);

    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Stockholm", out var zone) ? zone : TimeZoneInfo.Utc;

    public async Task HandleAsync(PurchaseCompleted purchase, CancellationToken cancellationToken)
    {
        var language = emailOptions.Value.Language;
        var line = purchase.Lines[0];
        var ev = await catalog.GetEventSummaryAsync(line.EventId, cancellationToken)
            ?? throw new InvalidOperationException($"Event {line.EventId} not found for receipt.");
        var tenant = await tenants.GetAsync(tenantContext.RequiredTenantId, cancellationToken);

        var link = await loginLinks.CreateReceiptLinkAsync(
            purchase.CustomerId, $"/e/{ev.Slug}/titta", LinkExpiry(ev.StartsAt, line.ReplayAccessDays), cancellationToken);

        string Label(string key, IReadOnlyDictionary<string, string>? values = null) => texts.Text(language, EmailTemplates.Receipt, key, values);
        var rows = new List<EmailRow>();
        foreach (var l in purchase.Lines)
        {
            var quantity = l.Quantity > 1 ? $"{l.Quantity} × " : "";
            rows.Add(new EmailRow($"{quantity}{l.EventTitle} – {l.TicketTypeName}", Money(l.UnitPrice * l.Quantity)));
        }

        var rate = purchase.Lines[0].VatRatePercent.ToString("0.##", Swedish);
        rows.Add(new EmailRow(Label("vat", new Dictionary<string, string> { ["rate"] = rate }), Money(purchase.VatAmount)));
        rows.Add(new EmailRow(Label("total"), Money(purchase.AmountTotal), Emphasize: true));
        rows.Add(new EmailRow(Label("seller"), SellerText()));
        rows.Add(new EmailRow(Label("organizer"), tenant?.Name ?? ""));
        rows.Add(new EmailRow(Label("orderNumber"), purchase.PurchaseId.ToString("N")[..12].ToUpperInvariant()));
        rows.Add(new EmailRow(Label("purchaseDate"), TimeZoneInfo.ConvertTime(purchase.CompletedAt, Stockholm).ToString("yyyy-MM-dd HH:mm", Swedish)));

        await email.SendAsync(
            new EmailRequest(purchase.Email, EmailTemplates.Receipt,
                new Dictionary<string, string> { ["eventTitle"] = ev.Title, ["link"] = link }, rows),
            cancellationToken);
    }

    private DateTimeOffset LinkExpiry(DateTimeOffset eventStartsAt, int? replayDays)
    {
        var accessEnds = eventStartsAt.AddDays(1 + (replayDays ?? 0));
        var latest = eventStartsAt + MaxLinkLifetimeAfterEvent;
        var earliest = time.GetUtcNow().AddDays(1);
        var expiry = accessEnds < latest ? accessEnds : latest;
        return expiry > earliest ? expiry : earliest;
    }

    private string SellerText()
    {
        var s = seller.Value;
        var parts = new[] { s.LegalName, s.OrganizationNumber is null ? null : $"org.nr {s.OrganizationNumber}", s.VatNumber is null ? null : $"momsreg.nr {s.VatNumber}", s.Address };
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string Money(long minorUnits) => (minorUnits / 100m).ToString("N2", Swedish) + " kr";
}
