using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

/// <summary>
/// Promotion codes are entered in Stripe Checkout; everything Fidoco books (purchase, VAT, commission,
/// settlement, receipt) follows what Stripe actually charged.
/// </summary>
public sealed class DiscountTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Sales(int TicketsSold, long Revenue);

    private sealed record Settlement(int TicketsSold, long DiscountAmount, long GrossAmount, long VatAmount, long NetAmount, long PlatformFee, long OrganizerShare, long OrganizerInvoiceTotal);

    private sealed record MyPurchase(long Amount);

    private sealed record SupportPurchase(long AmountPaid, long DiscountAmount, string? PromotionCode);

    private sealed record SupportDetails(List<SupportPurchase> Purchases);

    private sealed record Customer(Guid Id);

    [Fact]
    public async Task Checkout_offers_the_promotion_code_field()
    {
        var e = await factory.CreatePublishedEventAsync();

        var (checkout, _) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);

        Assert.True(factory.Payments.Checkouts[checkout.CheckoutId].Request.AllowPromotionCodes);
    }

    [Fact]
    public async Task Promotion_codes_can_be_turned_off()
    {
        using var noCodes = new NoPromotionCodesFactory();
        var e = await noCodes.CreatePublishedEventAsync();

        var (checkout, _) = await noCodes.StartCheckoutAsync(noCodes.ClientFor(e.Tenant.Host), e.TicketTypeId);

        Assert.False(noCodes.Payments.Checkouts[checkout.CheckoutId].Request.AllowPromotionCodes);
    }

    [Fact]
    public async Task Discounted_purchase_books_the_amount_paid_everywhere()
    {
        factory.Payments.PromotionCodes["promo_vip"] = "VIP50";
        var e = await factory.CreatePublishedEventAsync();
        var buyer = factory.ClientFor(e.Tenant.Host);
        var email = $"vip-{Guid.NewGuid():N}@example.com";

        var (_, sessionId) = await factory.StartCheckoutAsync(buyer, e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed",
            TestPayments.SessionObject(sessionId, email, amount: 14900, discount: 5000, promotionCode: "promo_vip")).EnsureSuccessAsync();
        await factory.DrainOutboxAsync();
        await factory.SignInCustomerAsync(buyer, email);

        var sales = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");
        var settlement = await e.Admin.GetFromJsonAsync<Settlement>($"/api/v1/admin/events/{e.Event.Id}/settlement");
        var csv = await e.Admin.GetStringAsync($"/api/v1/admin/events/{e.Event.Id}/settlement?format=csv");
        var mine = await buyer.GetFromJsonAsync<List<MyPurchase>>("/api/v1/me/purchases");
        var customer = (await e.Admin.GetFromJsonAsync<List<Customer>>($"/api/v1/admin/support/customers?email={email}"))!.Single();
        var support = await e.Admin.GetFromJsonAsync<SupportDetails>($"/api/v1/admin/support/customers/{customer.Id}");
        var receipt = factory.Emails.To(email).First();

        Assert.Equal(new Sales(1, 14900), sales);
        // 149,00 paid = 119,20 net + 29,80 VAT; commission 15 % of 119,20 = 17,88; organizer 101,32 + 25 % VAT.
        Assert.Equal(new Settlement(1, 5000, 14900, 2980, 11920, 1788, 10132, 12665), settlement);
        Assert.Contains(";Genomfört;1;149.00;29.80;119.20;17.88;101.32;50.00;VIP50", csv);
        Assert.Equal(14900, Assert.Single(mine!).Amount);
        Assert.Equal(new SupportPurchase(14900, 5000, "VIP50"), Assert.Single(support!.Purchases));
        Assert.Contains("Rabatt (VIP50): −50,00 kr", receipt.Text);
        Assert.Contains("Varav moms 25 %: 29,80 kr", receipt.Text);
        Assert.Contains("Totalt: 149,00 kr", receipt.Text);
    }

    [Fact]
    public async Task Free_with_a_full_discount_gives_access_and_counts_as_zero()
    {
        factory.Payments.PromotionCodes["promo_free"] = "GRATIS";
        var e = await factory.CreatePublishedEventAsync();
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        var buyer = factory.ClientFor(e.Tenant.Host);

        var (checkout, sessionId) = await factory.StartCheckoutAsync(buyer, e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed",
            TestPayments.SessionObject(sessionId, "free@example.com", paymentStatus: "no_payment_required", amount: 0, discount: 19900, promotionCode: "promo_free")).EnsureSuccessAsync();
        await factory.DrainOutboxAsync();
        var status = await buyer.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}");
        var session = await buyer.StartSessionOkAsync(e.Event.Id);
        var settlement = await e.Admin.GetFromJsonAsync<Settlement>($"/api/v1/admin/events/{e.Event.Id}/settlement");

        Assert.Equal(("completed", true), (status!.Status, status.SignedIn));
        Assert.Equal("live", session.Mode);
        Assert.Equal(new Settlement(1, 19900, 0, 0, 0, 0, 0, 0), settlement);
        Assert.Contains("Totalt: 0,00 kr", factory.Emails.To("free@example.com").Single().Text);
    }

    [Fact]
    public async Task Unknown_promotion_code_text_falls_back_to_its_id_without_failing_the_payment()
    {
        var e = await factory.CreatePublishedEventAsync();
        var email = $"x-{Guid.NewGuid():N}@example.com";

        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        var response = await factory.PostStripeWebhookAsync("checkout.session.completed",
            TestPayments.SessionObject(sessionId, email, amount: 17910, discount: 1990, promotionCode: "promo_unreachable"));
        var csv = await e.Admin.GetStringAsync($"/api/v1/admin/events/{e.Event.Id}/settlement?format=csv");

        response.EnsureSuccessStatusCode();
        Assert.Contains(";179.10;35.82;143.28;21.49;121.79;19.90;promo_unreachable", csv);
    }

    [Fact]
    public async Task Refunded_discounted_purchase_counts_for_nobody()
    {
        factory.Payments.PromotionCodes["promo_ref"] = "TIO";
        var e = await factory.CreatePublishedEventAsync();
        var (_, sessionId) = await factory.StartCheckoutAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed",
            TestPayments.SessionObject(sessionId, "ref@example.com", amount: 17910, discount: 1990, promotionCode: "promo_ref", paymentIntent: "pi_ref_" + sessionId)).EnsureSuccessAsync();

        await factory.PostStripeWebhookAsync("charge.refunded", TestPayments.ChargeObject("pi_ref_" + sessionId)).EnsureSuccessAsync();
        var settlement = await e.Admin.GetFromJsonAsync<Settlement>($"/api/v1/admin/events/{e.Event.Id}/settlement");

        Assert.Equal(new Settlement(0, 0, 0, 0, 0, 0, 0, 0), settlement);
    }

    private sealed class NoPromotionCodesFactory()
        : FidocoApiFactory("Sqlite", null, new Dictionary<string, string?> { ["Payments:AllowPromotionCodes"] = "false" });
}
