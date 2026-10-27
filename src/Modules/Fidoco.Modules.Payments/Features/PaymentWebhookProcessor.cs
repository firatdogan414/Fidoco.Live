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
    TimeProvider time,
    ILogger<PaymentWebhookProcessor> logger)
{
    public const string Provider = "stripe";

    public async Task ProcessAsync(PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        if (await db.ProcessedWebhookEvents.AnyAsync(w => w.Provider == Provider && w.EventId == webhook.EventId, cancellationToken))
        {
            return;
        }

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent { Provider = Provider, EventId = webhook.EventId, ProcessedAt = time.GetUtcNow() });

        switch (webhook.Type)
        {
            case PaymentWebhookType.CheckoutCompleted or PaymentWebhookType.CheckoutAsyncSucceeded when webhook.Paid:
                await InTenantOfCheckout(webhook, checkout => CompleteAsync(checkout, webhook, cancellationToken), cancellationToken);
                return;
            case PaymentWebhookType.CheckoutAsyncFailed or PaymentWebhookType.CheckoutExpired:
                await InTenantOfCheckout(webhook, checkout => CloseAsync(checkout, webhook, cancellationToken), cancellationToken);
                return;
            case PaymentWebhookType.ChargeRefunded when webhook.FullyRefunded:
                await RefundAsync(webhook, cancellationToken);
                return;
            default:
                // Unpaid "completed" (async methods: wait for async_payment_succeeded), partial refunds, other types.
                await SaveAsync(cancellationToken);
                return;
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

        var expected = checkout.UnitPrice * checkout.Quantity;
        var amount = webhook.AmountTotal ?? expected;
        if (amount != expected)
        {
            logger.LogWarning("Checkout {CheckoutId} paid {Amount} but expected {Expected}", checkout.Id, amount, expected);
        }

        Guid customerId;
        if (checkout.CustomerId is { } signedInCustomer)
        {
            customerId = signedInCustomer;
        }
        else
        {
            var account = await customers.GetOrCreateAsync(email, cancellationToken);
            customerId = account.Id;
            if (!account.Created)
            {
                // The e-mail typed on the payment page is unverified, so the paying browser must not be signed
                // into an account that already existed; the owner signs in with the receipt link instead.
                checkout.BrowserNonceHash = null;
            }
        }

        var now = time.GetUtcNow();
        var purchase = Purchase.Complete(checkout, customerId, email.Trim(), amount, webhook.PaymentIntentId, now);
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

    private async Task RefundAsync(PaymentWebhookEvent webhook, CancellationToken cancellationToken)
    {
        var tenantId = await db.Purchases.IgnoreQueryFilters()
            .Where(p => p.ProviderPaymentIntentId == webhook.PaymentIntentId && webhook.PaymentIntentId != null)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is null)
        {
            await SaveAsync(cancellationToken);
            return;
        }

        using var _ = tenant.Use(tenantId);
        var purchase = await db.Purchases.Include(p => p.Lines).SingleAsync(p => p.ProviderPaymentIntentId == webhook.PaymentIntentId, cancellationToken);
        purchase.Refund(time.GetUtcNow());
        await SaveAsync(cancellationToken);
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
