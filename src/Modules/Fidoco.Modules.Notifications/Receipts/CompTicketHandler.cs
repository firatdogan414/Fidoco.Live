using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Notifications;
using Fidoco.Contracts.Playback;
using Fidoco.SharedKernel.Domain;

namespace Fidoco.Modules.Notifications.Receipts;

/// <summary>
/// E-mails the recipient of a complimentary ticket a sign-in link that works like a receipt's (reusable
/// until access ends). There is no receipt: nothing was sold.
/// </summary>
internal sealed class CompTicketHandler(
    IEmailDispatcher email,
    ICustomerLoginLinks loginLinks,
    ICatalogQueries catalog,
    TimeProvider time) : IDomainEventHandler<ComplimentaryTicketGranted>
{
    public async Task HandleAsync(ComplimentaryTicketGranted comp, CancellationToken cancellationToken)
    {
        var ev = await catalog.GetEventSummaryAsync(comp.EventId, cancellationToken)
            ?? throw new InvalidOperationException($"Event {comp.EventId} not found for comp ticket.");
        var link = await loginLinks.CreateReceiptLinkAsync(
            comp.CustomerId, $"/e/{ev.Slug}/titta", PurchaseReceiptHandler.LinkExpiry(ev.StartsAt, comp.ReplayAccessDays, time.GetUtcNow()), cancellationToken);

        await email.SendAsync(
            new EmailRequest(comp.Email, EmailTemplates.CompTicket,
                new Dictionary<string, string> { ["eventTitle"] = ev.Title, ["ticketTypeName"] = comp.TicketTypeName, ["link"] = link }),
            cancellationToken);
    }
}
