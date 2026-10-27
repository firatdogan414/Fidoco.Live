namespace Fidoco.Modules.Payments.Infrastructure.Gateway;

internal sealed record CheckoutRequest(
    Guid CheckoutId,
    Guid TenantId,
    string ProductName,
    long UnitAmount,
    string Currency,
    int Quantity,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt);

internal sealed record CheckoutCreated(string ProviderSessionId, string RedirectUrl);

/// <summary>Thin abstraction over the payment provider's hosted checkout (Stripe Checkout).</summary>
internal interface IPaymentGateway
{
    Task<CheckoutCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);
}

internal enum PaymentWebhookType
{
    CheckoutCompleted,
    CheckoutAsyncSucceeded,
    CheckoutAsyncFailed,
    CheckoutExpired,
    ChargeRefunded,
    Ignored,
}

/// <summary>Provider-neutral view of a payment webhook.</summary>
internal sealed record PaymentWebhookEvent(
    string EventId,
    PaymentWebhookType Type,
    string? SessionId = null,
    bool Paid = false,
    string? PaymentIntentId = null,
    string? Email = null,
    long? AmountTotal = null,
    bool FullyRefunded = false);

internal sealed class InvalidWebhookSignatureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Verifies the signature and parses a webhook payload (pure, no network).</summary>
internal interface IPaymentWebhookParser
{
    PaymentWebhookEvent Parse(string payload, string signatureHeader);
}

internal sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>"Stripe" (default) or "Fake" (Development only: a local fake checkout page).</summary>
    public string Provider { get; set; } = "Stripe";

    /// <summary>How long a checkout (and a future capacity reservation) is held. Stripe's minimum is 30 minutes.</summary>
    public TimeSpan CheckoutLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

internal sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>sk_test_... during development, sk_live_... in production.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>whsec_... of the webhook endpoint (or of "stripe listen" locally).</summary>
    public string WebhookSecret { get; set; } = "";
}
