using System.Text.Json;
using Fidoco.Contracts.Payments;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Stripe;

namespace Fidoco.UnitTests.Payments;

public sealed class PurchaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private static CheckoutSession Checkout(decimal commissionPercent = 15m, long fixedPerTicket = 0) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        EventSlug = "afn-12",
        EventTitle = "AFN 12",
        TicketTypeId = Guid.NewGuid(),
        TicketTypeName = "Livestream",
        UnitPrice = 19900,
        Currency = "SEK",
        VatRatePercent = 25m,
        IncludesLive = true,
        ReplayAccessDays = 7,
        CommissionPercent = commissionPercent,
        CommissionFixedAmountPerTicket = fixedPerTicket,
    };

    [Fact]
    public void Full_price_purchase_has_no_discount()
    {
        var purchase = Purchase.Complete(Checkout(), Guid.NewGuid(), "a@example.com", new PaidAmount(19900), "pi_1", Now);

        var line = Assert.Single(purchase.Lines);
        Assert.Equal((19900L, 3980L, 0L, (string?)null), (purchase.AmountTotal, purchase.VatAmount, purchase.DiscountAmount, purchase.PromotionCode));
        Assert.Equal((19900L, 0L, 15920L, 2388L), (line.GrossAmount, line.DiscountAmount, line.NetAmount, line.PlatformFeeAmount));
    }

    [Fact]
    public void Vat_and_commission_follow_the_discounted_amount()
    {
        var purchase = Purchase.Complete(Checkout(), Guid.NewGuid(), "a@example.com", new PaidAmount(14900, "VIP50"), "pi_1", Now);

        var line = Assert.Single(purchase.Lines);
        Assert.Equal((14900L, 2980L, 5000L, "VIP50"), (purchase.AmountTotal, purchase.VatAmount, purchase.DiscountAmount, purchase.PromotionCode));
        Assert.Equal((19900L, 14900L, 5000L, 11920L, 1788L), (line.UnitPrice, line.GrossAmount, line.DiscountAmount, line.NetAmount, line.PlatformFeeAmount));
        var completed = Assert.IsType<PurchaseCompleted>(Assert.Single(purchase.DomainEvents));
        Assert.Equal((14900L, 2980L, 5000L, "VIP50"), (completed.AmountTotal, completed.VatAmount, completed.DiscountAmount, completed.PromotionCode));
    }

    [Fact]
    public void Free_purchase_has_no_vat_and_no_commission_even_with_a_fixed_fee()
    {
        var purchase = Purchase.Complete(Checkout(fixedPerTicket: 500), Guid.NewGuid(), "a@example.com", new PaidAmount(0, "GRATIS"), null, Now);

        var line = Assert.Single(purchase.Lines);
        Assert.Equal((0L, 0L, 19900L), (purchase.AmountTotal, purchase.VatAmount, purchase.DiscountAmount));
        Assert.Equal((0L, 0L), (line.NetAmount, line.PlatformFeeAmount));
    }
}

public sealed class StripeWebhookParserTests
{
    private const string Secret = "whsec_unit";

    [Fact]
    public void Checkout_completed_carries_amount_paid_discount_and_promotion_code()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var parser = new StripeWebhookParser(Options.Create(new StripeOptions { WebhookSecret = Secret }), time);
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_1",
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            created = time.GetUtcNow().ToUnixTimeSeconds(),
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = "cs_1",
                    @object = "checkout.session",
                    payment_status = "paid",
                    payment_intent = "pi_1",
                    amount_subtotal = 19900,
                    amount_total = 14900,
                    total_details = new { amount_discount = 5000, amount_shipping = 0, amount_tax = 0 },
                    discounts = new[] { new { coupon = "co_1", promotion_code = "promo_1" } },
                    currency = "sek",
                    customer_details = new { email = "a@example.com" },
                },
            },
        });

        var webhook = parser.Parse(payload, EventUtility.GenerateSignatureHeader(payload, Secret, time.GetUtcNow().ToUnixTimeSeconds()));

        Assert.Equal(PaymentWebhookType.CheckoutCompleted, webhook.Type);
        Assert.Equal((14900L, 5000L, "promo_1"), (webhook.AmountTotal, webhook.AmountDiscount, webhook.PromotionCodeId));
    }
}
