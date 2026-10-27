using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;
using Fidoco.Modules.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests;

public sealed class PaymentsTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Sales(Guid EventId, int TicketsSold, int TicketsRefunded, long Revenue);

    private sealed record SessionScope(string Scope);

    private sealed record MyPurchase(Guid PurchaseId, Guid EventId, string EventTitle, long Amount, string Status);

    [Fact]
    public async Task Anonymous_buyer_pays_is_signed_in_and_gets_a_receipt()
    {
        var e = await factory.CreatePublishedEventAsync();
        var buyer = factory.ClientFor(e.Tenant.Host);
        var email = $"buyer-{Guid.NewGuid():N}@example.com";

        var (checkout, sessionId) = await factory.StartCheckoutAsync(buyer, e.TicketTypeId);
        var request = factory.Payments.Checkouts[checkout.CheckoutId].Request;
        var pending = await buyer.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}");

        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, email)).EnsureSuccessAsync();
        await factory.DrainOutboxAsync();
        var completed = await buyer.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}");
        var session = await buyer.GetFromJsonAsync<SessionScope>("/api/v1/auth/session");
        var me = await buyer.GetAsync("/api/v1/me");

        Assert.Equal(19900, request.UnitAmount);
        Assert.Equal("SEK", request.Currency);
        Assert.Equal($"https://{e.Tenant.Host}/e/{e.Event.Slug}/tack?c={checkout.CheckoutId}", request.SuccessUrl);
        Assert.Equal("pending", pending!.Status);
        Assert.Equal("completed", completed!.Status);
        Assert.True(completed.SignedIn);
        Assert.Equal("purchase", session!.Scope);
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);

        var receipt = Assert.Single(factory.Emails.To(email));
        Assert.Equal($"Kvitto: {e.Event.Title}", receipt.Subject);
        Assert.Contains("Totalt: 199,00 kr", receipt.Text);
        Assert.Contains("Varav moms 25 %: 39,80 kr", receipt.Text);
        Assert.Contains("Säljare: Fidoco AB, org.nr 559999-9999", receipt.Text);
        Assert.Contains($"Arrangör: {e.Tenant.Name}", receipt.Text);
        Assert.Contains($"https://{e.Tenant.Host}/auth/verifiera?token=", receipt.Text);
    }

    [Fact]
    public async Task Receipt_link_signs_in_on_another_device_and_can_be_reused()
    {
        var e = await factory.CreatePublishedEventAsync();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, email);
        var token = TestIdentity.ExtractToken(factory.Emails.To(email).Single().Text);

        var phone = factory.ClientFor(e.Tenant.Host);
        var first = await phone.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var again = await factory.ClientFor(e.Tenant.Host).PostAsJsonAsync("/api/v1/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains($"/e/{e.Event.Slug}/titta", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Another_browser_sees_completed_but_is_not_signed_in()
    {
        var e = await factory.CreatePublishedEventAsync();
        var (checkout, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "x@example.com")).EnsureSuccessAsync();

        var other = factory.ClientFor(e.Tenant.Host);
        var status = await other.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}");

        Assert.Equal("completed", status!.Status);
        Assert.False(status.SignedIn);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Paying_with_someone_elses_email_lets_the_browser_watch_its_purchase_but_not_use_the_account()
    {
        // The e-mail typed on the payment page is unverified: anyone can enter someone else's address.
        var e = await factory.CreatePublishedEventAsync();
        var other = await factory.CreatePublishedEventAsync(e.Tenant);
        var victimEmail = $"victim-{Guid.NewGuid():N}@example.com";
        var victim = factory.ClientFor(e.Tenant.Host);
        await factory.BuyAsync(victim, other.TicketTypeId, victimEmail);
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        await factory.PostMuxWebhookAsync("video.live_stream.active", other.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        var attacker = factory.ClientFor(e.Tenant.Host);

        var status = await factory.BuyAsync(attacker, e.TicketTypeId, victimEmail);

        Assert.True(status.SignedIn);
        await attacker.StartSessionOkAsync(e.Event.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.PostAsync($"/api/v1/events/{other.Event.Id}/playback-sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await attacker.GetAsync("/api/v1/me/purchases")).StatusCode);
        var delete = await attacker.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new { confirmEmail = victimEmail }) });
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        // Buying again from the limited session is anonymous: the victim's address is not pre-filled.
        var (again, _) = await factory.StartCheckoutAsync(attacker, other.TicketTypeId);
        Assert.Null(factory.Payments.Checkouts[again.CheckoutId].Request.CustomerEmail);

        // The address owner still has full access with the magic link.
        await factory.SignInCustomerAsync(victim, victimEmail);
        Assert.Equal(2, (await victim.GetFromJsonAsync<List<MyPurchase>>("/api/v1/me/purchases"))!.Count);
    }

    [Fact]
    public async Task Webhook_is_idempotent_for_redelivery_and_for_different_events_of_the_same_session()
    {
        var e = await factory.CreatePublishedEventAsync();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        var session = TestPayments.SessionObject(sessionId, email);

        var responses = new[]
        {
            await factory.PostStripeWebhookAsync("checkout.session.completed", session, eventId: "evt_same"),
            await factory.PostStripeWebhookAsync("checkout.session.completed", session, eventId: "evt_same"),
            await factory.PostStripeWebhookAsync("checkout.session.async_payment_succeeded", session),
        };
        await factory.DrainOutboxAsync();

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await PurchaseCount(e.Tenant.Id));
        Assert.Single(factory.Emails.To(email));
        var sales = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");
        Assert.Equal((1, 19900L), (sales!.TicketsSold, sales.Revenue));
    }

    [Fact]
    public async Task Concurrent_deliveries_create_one_purchase()
    {
        var e = await factory.CreatePublishedEventAsync();
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        var session = TestPayments.SessionObject(sessionId, $"c-{Guid.NewGuid():N}@example.com");

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            factory.PostStripeWebhookAsync("checkout.session.completed", session, eventId: $"evt_concurrent_{i}_{sessionId}")));

        Assert.Equal(1, await PurchaseCount(e.Tenant.Id));
    }

    [Fact]
    public async Task Webhook_with_invalid_signature_is_rejected_without_side_effects()
    {
        var e = await factory.CreatePublishedEventAsync();
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);

        var response = await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "x@example.com"), secret: "whsec_wrong");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await PurchaseCount(e.Tenant.Id));
    }

    [Fact]
    public async Task Async_payment_completes_only_when_paid()
    {
        var e = await factory.CreatePublishedEventAsync();
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);

        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "swish@example.com", paymentStatus: "unpaid"));
        var afterUnpaid = await PurchaseCount(e.Tenant.Id);
        await factory.PostStripeWebhookAsync("checkout.session.async_payment_succeeded", TestPayments.SessionObject(sessionId, "swish@example.com"));

        Assert.Equal(0, afterUnpaid);
        Assert.Equal(1, await PurchaseCount(e.Tenant.Id));
    }

    [Fact]
    public async Task Expired_checkout_is_marked_expired()
    {
        var e = await factory.CreatePublishedEventAsync();
        var client = factory.ClientFor(e.Tenant.Host);
        var (checkout, sessionId) = await factory.StartCheckoutAsync(client, e.TicketTypeId);

        await factory.PostStripeWebhookAsync("checkout.session.expired", TestPayments.SessionObject(sessionId, null, paymentStatus: "unpaid"));

        var status = await client.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}");
        Assert.Equal("expired", status!.Status);
    }

    [Fact]
    public async Task Full_refund_marks_purchase_refunded_and_removes_it_from_revenue()
    {
        var e = await factory.CreatePublishedEventAsync();
        var buyer = factory.ClientFor(e.Tenant.Host);
        var (_, sessionId) = await factory.StartCheckoutAsync(buyer, e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "refund@example.com", paymentIntent: "pi_refund_" + sessionId));

        await factory.PostStripeWebhookAsync("charge.refunded", TestPayments.ChargeObject("pi_refund_" + sessionId, refunded: false));
        var afterPartial = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");
        await factory.PostStripeWebhookAsync("charge.refunded", TestPayments.ChargeObject("pi_refund_" + sessionId));
        var afterFull = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");

        Assert.Equal(1, afterPartial!.TicketsSold);
        Assert.Equal((0, 1, 0L), (afterFull!.TicketsSold, afterFull.TicketsRefunded, afterFull.Revenue));
    }

    [Fact]
    public async Task Signed_in_buyer_with_a_ticket_cannot_buy_again()
    {
        var e = await factory.CreatePublishedEventAsync();
        var buyer = factory.ClientFor(e.Tenant.Host);
        var email = $"twice-{Guid.NewGuid():N}@example.com";
        await factory.BuyAsync(buyer, e.TicketTypeId, email);
        await factory.SignInCustomerAsync(buyer, email);

        var response = await buyer.PostAsJsonAsync("/api/v1/checkout", new { ticketTypeId = e.TicketTypeId });
        var purchases = await buyer.GetFromJsonAsync<List<MyPurchase>>("/api/v1/me/purchases");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already_purchased", await response.Content.ReadAsStringAsync());
        Assert.Equal(e.Event.Id, Assert.Single(purchases!).EventId);
    }

    [Fact]
    public async Task Draft_event_cannot_be_bought()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);
        var draft = await admin.CreateEventAsync();

        var response = await factory.ClientFor(tenant.Host).PostAsJsonAsync("/api/v1/checkout", new { ticketTypeId = draft.Ticket!.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("event_not_published", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ticket_and_checkout_of_another_tenant_are_not_found()
    {
        var a = await factory.CreatePublishedEventAsync();
        var b = await factory.CreateTenantAsync();
        var (checkout, _) = await factory.StartCheckoutAsync(factory.ClientFor(a.Tenant.Host), a.TicketTypeId);
        var onB = factory.ClientFor(b.Host);

        var buy = await onB.PostAsJsonAsync("/api/v1/checkout", new { ticketTypeId = a.TicketTypeId });
        var status = await onB.GetAsync($"/api/v1/checkout/{checkout.CheckoutId}");

        Assert.Equal(HttpStatusCode.NotFound, buy.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }

    private async Task<int> PurchaseCount(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await db.Purchases.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
    }
}
