using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Fidoco.Modules.Payments.Infrastructure.Gateway;

/// <summary>
/// Stripe Checkout. Payment methods are not hard-coded: they follow the Stripe Dashboard settings
/// (dynamic payment methods), so Swish is enabled there without code changes.
/// </summary>
internal sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    public async Task<CheckoutCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = request.TenantId.ToString(),
            ["checkoutId"] = request.CheckoutId.ToString(),
        };
        var service = new SessionService(new StripeClient(options.Value.SecretKey));
        var session = await service.CreateAsync(
            new SessionCreateOptions
            {
                Mode = "payment",
                Locale = "sv",
                ClientReferenceId = request.CheckoutId.ToString(),
                CustomerEmail = request.CustomerEmail,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                ExpiresAt = request.ExpiresAt.UtcDateTime,
                Metadata = metadata,
                PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Quantity = request.Quantity,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = request.Currency.ToLowerInvariant(),
                            UnitAmount = request.UnitAmount,
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.ProductName },
                        },
                    },
                ],
            },
            new RequestOptions { IdempotencyKey = $"checkout-{request.CheckoutId}" },
            cancellationToken);
        return new CheckoutCreated(session.Id, session.Url);
    }
}

internal sealed class StripeWebhookParser(IOptions<StripeOptions> options, TimeProvider time) : IPaymentWebhookParser
{
    public const long ToleranceSeconds = 300;

    public PaymentWebhookEvent Parse(string payload, string signatureHeader)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload, signatureHeader, options.Value.WebhookSecret, ToleranceSeconds,
                time.GetUtcNow().ToUnixTimeSeconds(), throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new InvalidWebhookSignatureException("Invalid Stripe webhook signature.", ex);
        }

        return stripeEvent.Type switch
        {
            "checkout.session.completed" => FromSession(stripeEvent, PaymentWebhookType.CheckoutCompleted),
            "checkout.session.async_payment_succeeded" => FromSession(stripeEvent, PaymentWebhookType.CheckoutAsyncSucceeded),
            "checkout.session.async_payment_failed" => FromSession(stripeEvent, PaymentWebhookType.CheckoutAsyncFailed),
            "checkout.session.expired" => FromSession(stripeEvent, PaymentWebhookType.CheckoutExpired),
            "charge.refunded" when stripeEvent.Data.Object is Charge charge => new PaymentWebhookEvent(
                stripeEvent.Id, PaymentWebhookType.ChargeRefunded, PaymentIntentId: charge.PaymentIntentId, FullyRefunded: charge.Refunded),
            _ => new PaymentWebhookEvent(stripeEvent.Id, PaymentWebhookType.Ignored),
        };
    }

    private static PaymentWebhookEvent FromSession(Event stripeEvent, PaymentWebhookType type)
    {
        if (stripeEvent.Data.Object is not Session session)
        {
            return new PaymentWebhookEvent(stripeEvent.Id, PaymentWebhookType.Ignored);
        }

        return new PaymentWebhookEvent(
            stripeEvent.Id,
            type,
            SessionId: session.Id,
            Paid: session.PaymentStatus is "paid" or "no_payment_required",
            PaymentIntentId: session.PaymentIntentId,
            Email: session.CustomerDetails?.Email ?? session.CustomerEmail,
            AmountTotal: session.AmountTotal);
    }
}
