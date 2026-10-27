using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Fidoco.SharedKernel.Observability;
using Fidoco.SharedKernel.Tenancy;
using Fidoco.SharedKernel.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fidoco.Modules.Payments.Features;

/// <summary>
/// Applies payment webhooks. Idempotent in three ways: the provider event id is stored in the same
/// transaction, a completed checkout is never completed again, and a purchase is unique per checkout.
/// </summary>
internal sealed class PaymentWebhookProcessor(
    PaymentsDbContext db,
    ITenantContext tenant,
    ICustomerAccounts customers,
    ITicketAvailability availability,
    IPaymentGateway gateway,
    TimeProvider time,
    ILogger<PaymentWebhookProcessor> logger)
{
    public const string Provider = "stripe";

    /// <summary>
    /// Returns false when the event cannot be applied yet and must be delivered again (nothing is stored,
    /// so the provider's retry processes it from scratch).
    /// </summary>
    public async Task<bool> ProcessAsync(PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        if (await db.ProcessedWebhookEvents.AnyAsync(w => w.Provider == Provider && w.EventId == webhook.EventId, cancellationToken))
        {
            return true;
        }

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent { Provider = Provider, EventId = webhook.EventId, ProcessedAt = time.GetUtcNow() });

        switch (webhook.Type)
        {
            case PaymentWebhookType.CheckoutCompleted or PaymentWebhookType.CheckoutAsyncSucceeded when webhook.Paid:
                await InTenantOfCheckout(webhook, checkout => CompleteAsync(checkout, webhook, cancellationToken), cancellationToken);
                return true;
            case PaymentWebhookType.CheckoutAsyncFailed or PaymentWebhookType.CheckoutExpired:
                await InTenantOfCheckout(webhook, checkout => CloseAsync(checkout, webhook, cancellationToken), cancellationToken);
                return true;
            case PaymentWebhookType.ChargeRefunded when webhook.FullyRefunded:
                return await RefundAsync(webhook, cancellationToken);
            default:
                // Unpaid "completed" (async methods: wait for async_payment_succeeded), partial refunds, other types.
                await SaveAsync(cancellationToken);
                return true;
        }
    }

    private async Task InTenantOfCheckout(PaymentWebhookEvent webhook, Func<CheckoutSession, Task> action, CancellationToken cancellationToken)
    {
        // Webhooks arrive on the platform host: find the owning tenant explicitly, then work inside it.
        var tenantId = await db.CheckoutSessions.IgnoreQueryFilters()
            .Where(c => c.ProviderSessionId == webhook.SessionId)
            .Select(c => (Guid?)c.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
        if (tenantId is null)
        {
            logger.LogWarning("Payment webhook {EventId} for unknown checkout session {SessionId}", webhook.EventId, webhook.SessionId);
            await SaveAsync(cancellationToken);
            return;
        }

        using var _ = tenant.Use(tenantId);
        var checkout = await db.CheckoutSessions.SingleAsync(c => c.ProviderSessionId == webhook.SessionId, cancellationToken);
        await action(checkout);
    }

    private async Task CompleteAsync(CheckoutSession checkout, PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        if (checkout.Status == CheckoutStatus.Completed)
        {
            await SaveAsync(cancellationToken);
            return;
        }

        var email = webhook.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException($"Paid checkout {checkout.Id} has no customer e-mail.");
        }

        // The purchase is booked at what Stripe charged, which is below the list price when a promotion code was used.
        var listPrice = checkout.UnitPrice * checkout.Quantity;
        var amount = webhook.AmountTotal ?? listPrice;
        var discount = webhook.AmountDiscount ?? 0;
        if (amount + discount != listPrice)
        {
            logger.LogWarning(
                "Checkout {CheckoutId} paid {Amount} with discount {Discount} but the list price is {ListPrice}; booking the amount paid",
                checkout.Id, amount, discount, listPrice);
        }

        var promotionCode = webhook.PromotionCodeId is { } promotionCodeId ? await PromotionCodeAsync(promotionCodeId, cancellationToken) : null;
        var customerId = checkout.CustomerId ?? await customers.GetOrCreateAsync(email, cancellationToken);
        var now = time.GetUtcNow();
        var purchase = Purchase.Complete(checkout, customerId, email.Trim(), new PaidAmount(amount, promotionCode), webhook.PaymentIntentId, now);
        db.Purchases.Add(purchase);
        checkout.Status = CheckoutStatus.Completed;
        checkout.CompletedAt = now;
        checkout.CustomerId = customerId;
        checkout.PurchaseId = purchase.Id;
        if (checkout.ReservationId is { } reservationId)
        {
            await availability.ConfirmAsync(reservationId, cancellationToken);
        }

        if (await SaveAsync(cancellationToken))
        {
            FidocoTelemetry.PurchasesCompleted.Add(1);
        }
    }

    /// <summary>The code text for reports; the id when Stripe cannot be asked right now (never fails the payment).</summary>
    private async Task<string> PromotionCodeAsync(string promotionCodeId, CancellationToken cancellationToken)
    {
        try
        {
            return await gateway.GetPromotionCodeAsync(promotionCodeId, cancellationToken) ?? promotionCodeId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not look up promotion code {PromotionCodeId}; storing the id", promotionCodeId);
            return promotionCodeId;
        }
    }

    private async Task CloseAsync(CheckoutSession checkout, PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        if (checkout.Status == CheckoutStatus.Pending)
        {
            checkout.Status = webhook.Type == PaymentWebhookType.CheckoutExpired ? CheckoutStatus.Expired : CheckoutStatus.Failed;
            if (checkout.ReservationId is { } reservationId)
            {
                await availability.ReleaseAsync(reservationId, cancellationToken);
            }
        }

        await SaveAsync(cancellationToken);
    }

    private async Task<bool> RefundAsync(PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        if (webhook.PaymentIntentId is null)
        {
            await SaveAsync(cancellationToken);
            return true;
        }

        var tenantId = await db.Purchases.IgnoreQueryFilters()
            .Where(p => p.ProviderPaymentIntentId == webhook.PaymentIntentId)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is null)
        {
            // The refund arrived before the purchase (Stripe does not order events, and the completion may
            // still be retrying). Marking it processed would lose it, so ask Stripe to deliver it again.
            logger.LogWarning("Refund {EventId} for unknown payment {PaymentIntentId}; asking for redelivery", webhook.EventId, webhook.PaymentIntentId);
            return false;
        }

        using var _ = tenant.Use(tenantId);
        var purchase = await db.Purchases.Include(p => p.Lines).SingleAsync(p => p.ProviderPaymentIntentId == webhook.PaymentIntentId, cancellationToken);
        purchase.Refund(time.GetUtcNow());
        await SaveAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns false when a concurrent delivery already stored the same event or purchase.</summary>
    private async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(ex, "Payment webhook already processed by a concurrent delivery");
            return false;
        }
    }
}
