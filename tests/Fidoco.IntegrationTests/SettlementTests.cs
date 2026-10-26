using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class SettlementTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Settlement(int TicketsSold, int TicketsRefunded, long GrossAmount, long VatAmount, long NetAmount, long PlatformFee, long OrganizerShare, long OrganizerVat, long OrganizerInvoiceTotal);

    [Fact]
    public async Task Settlement_splits_net_revenue_between_fidoco_and_the_organizer_and_ignores_refunds()
    {
        // Test tenants have the default terms: 15 % commission, VAT-registered organizer.
        var e = await factory.CreatePublishedEventAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "a@example.com");
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "b@example.com");
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "c@example.com", paymentIntent: "pi_settle_" + sessionId));
        await factory.PostStripeWebhookAsync("charge.refunded", TestPayments.ChargeObject("pi_settle_" + sessionId));

        var s = await e.Admin.GetFromJsonAsync<Settlement>($"/api/v1/admin/events/{e.Event.Id}/settlement");

        // Per ticket: 199,00 gross = 159,20 net + 39,80 VAT; commission 15 % of 159,20 = 23,88.
        Assert.Equal(new Settlement(2, 1, 39800, 7960, 31840, 4776, 27064, 6766, 33830), s);
    }

    [Fact]
    public async Task Settlement_is_available_as_csv()
    {
        var e = await factory.CreatePublishedEventAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "csv@example.com");

        var response = await e.Admin.GetAsync($"/api/v1/admin/events/{e.Event.Id}/settlement?format=csv");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Summa;;;1;199.00;39.80;159.20;23.88;135.32", csv);
        Assert.Contains("Att fakturera Fidoco;;;;;;;;169.15", csv);
    }

    [Fact]
    public async Task Settlement_of_another_tenants_event_is_not_found()
    {
        var a = await factory.CreatePublishedEventAsync();
        var b = await factory.CreatePublishedEventAsync();

        var response = await a.Admin.GetAsync($"/api/v1/admin/events/{b.Event.Id}/settlement");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
