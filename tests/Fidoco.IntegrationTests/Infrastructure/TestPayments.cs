using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Stripe;

namespace Fidoco.IntegrationTests.Infrastructure;

/// <summary>Records checkout requests instead of calling Stripe.</summary>
internal sealed class TestPaymentGateway : IPaymentGateway
{
    public ConcurrentDictionary<Guid, (CheckoutRequest Request, string SessionId)> Checkouts { get; } = new();

    /// <summary>Promotion code ids known to the fake Stripe; unknown ids make the lookup fail.</summary>
    public ConcurrentDictionary<string, string> PromotionCodes { get; } = new();

    public Task<string?> GetPromotionCodeAsync(string promotionCodeId, CancellationToken cancellationToken) =>
        PromotionCodes.TryGetValue(promotionCodeId, out var code)
            ? Task.FromResult<string?>(code)
            : throw new HttpRequestException("Stripe is unavailable");

    public Task<CheckoutCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        Checkouts[request.CheckoutId] = (request, sessionId);
        return Task.FromResult(new CheckoutCreated(sessionId, $"https://checkout.stripe.test/{sessionId}"));
    }
}

public sealed record CheckoutStarted(Guid CheckoutId, string RedirectUrl);

public sealed record CheckoutStatus(Guid CheckoutId, string Status, Guid EventId, string EventSlug, bool SignedIn);

internal static class TestPayments
{
    public const string WebhookSecret = "whsec_test_secret";

    public static async Task<(CheckoutStarted Checkout, string SessionId)> StartCheckoutAsync(this FidocoApiFactory factory, HttpClient client, Guid ticketTypeId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/checkout", new { ticketTypeId }).EnsureSuccessAsync();
        var started = (await response.Content.ReadFromJsonAsync<CheckoutStarted>())!;
        return (started, factory.Payments.Checkouts[started.CheckoutId].SessionId);
    }

    /// <summary>Checkout + paid webhook + outbox drain: the buyer has a purchase, an entitlement and a receipt.</summary>
    public static async Task<CheckoutStatus> BuyAsync(this FidocoApiFactory factory, HttpClient client, Guid ticketTypeId, string email)
    {
        var (checkout, sessionId) = await factory.StartCheckoutAsync(client, ticketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed", SessionObject(sessionId, email)).EnsureSuccessAsync();
        await factory.DrainOutboxAsync();
        return (await client.GetFromJsonAsync<CheckoutStatus>($"/api/v1/checkout/{checkout.CheckoutId}"))!;
    }

    public static object SessionObject(
        string sessionId, string? email, string paymentStatus = "paid", long amount = 19900, string? paymentIntent = null, long discount = 0, string? promotionCode = null) => new
    {
        id = sessionId,
        @object = "checkout.session",
        payment_status = paymentStatus,
        payment_intent = paymentStatus == "no_payment_required" ? null : paymentIntent ?? "pi_" + sessionId,
        amount_subtotal = amount + discount,
        amount_total = amount,
        total_details = new { amount_discount = discount, amount_shipping = 0, amount_tax = 0 },
        discounts = promotionCode is null ? Array.Empty<object>() : [new { coupon = "coupon_" + promotionCode, promotion_code = promotionCode }],
        currency = "sek",
        customer_details = new { email },
        metadata = new Dictionary<string, string>(),
    };

    public static object ChargeObject(string paymentIntent, bool refunded = true) => new
    {
        id = "ch_" + Guid.NewGuid().ToString("N"),
        @object = "charge",
        payment_intent = paymentIntent,
        refunded,
        amount_refunded = refunded ? 19900 : 1000,
    };

    /// <summary>Posts a Stripe-signed webhook (real signature verification, no network).</summary>
    public static Task<HttpResponseMessage> PostStripeWebhookAsync(this FidocoApiFactory factory, string type, object dataObject, string? eventId = null, string? secret = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = eventId ?? "evt_" + Guid.NewGuid().ToString("N"),
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            created = factory.Time.GetUtcNow().ToUnixTimeSeconds(),
            type,
            data = new { @object = dataObject },
        });
        var timestamp = factory.Time.GetUtcNow().ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", EventUtility.GenerateSignatureHeader(payload, secret ?? WebhookSecret, timestamp));
        return factory.ClientFor(FidocoApiFactory.PlatformHost).SendAsync(request);
    }

    public static string Format(long minor) => (minor / 100m).ToString("N2", CultureInfo.GetCultureInfo("sv-SE")) + " kr";
}
