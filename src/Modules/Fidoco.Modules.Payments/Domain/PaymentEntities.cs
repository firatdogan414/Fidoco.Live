using Fidoco.Contracts.Payments;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Payments.Domain;

internal enum CheckoutStatus
{
    Pending,
    Completed,
    Expired,
    Failed,
}

/// <summary>
/// A started checkout with a snapshot of what is being bought, so a price change during payment cannot
/// alter the order. Links the browser (nonce) to the purchase for sign-in right after payment.
/// </summary>
internal sealed class CheckoutSession : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? CustomerId { get; set; }

    public string? ProviderSessionId { get; set; }

    public CheckoutStatus Status { get; set; } = CheckoutStatus.Pending;

    /// <summary>SHA-256 of the nonce in the buyer's HttpOnly checkout cookie.</summary>
    public string? BrowserNonceHash { get; set; }

    public DateTimeOffset? NonceUsedAt { get; set; }

    /// <summary>Capacity reservation (always null in phase 1).</summary>
    public Guid? ReservationId { get; set; }

    public Guid EventId { get; set; }

    public required string EventSlug { get; set; }

    public required string EventTitle { get; set; }

    public Guid TicketTypeId { get; set; }

    public required string TicketTypeName { get; set; }

    public int Quantity { get; set; } = 1;

    public long UnitPrice { get; set; }

    public required string Currency { get; set; }

    public decimal VatRatePercent { get; set; }

    public bool IncludesLive { get; set; }

    public int? ReplayAccessDays { get; set; }

    /// <summary>The tenant's commercial terms when the checkout started (snapshot).</summary>
    public decimal CommissionPercent { get; set; }

    public long CommissionFixedAmountPerTicket { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? PurchaseId { get; set; }
}

internal enum PurchaseStatus
{
    Completed,
    Refunded,
}

/// <summary>A paid order. Kept for bookkeeping; CustomerId is cleared when a customer's data is erased.</summary>
internal sealed class Purchase : AggregateRoot, ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CheckoutSessionId { get; set; }

    public Guid? CustomerId { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Completed;

    /// <summary>Total actually paid including VAT (after discounts), in minor units, as charged by Stripe.</summary>
    public long AmountTotal { get; set; }

    public long VatAmount { get; set; }

    /// <summary>List price minus what was paid: the discount from a promotion code (0 without one).</summary>
    public long DiscountAmount { get; set; }

    /// <summary>The promotion code the buyer entered in Stripe Checkout (the code text, or its id if unknown).</summary>
    public string? PromotionCode { get; set; }

    public required string Currency { get; set; }

    public string? ProviderPaymentIntentId { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public DateTimeOffset? RefundedAt { get; set; }

    public List<PurchaseLine> Lines { get; set; } = [];

    /// <summary>
    /// Records a paid checkout. Every amount (VAT, net, commission) is based on what Stripe actually charged,
    /// so a promotion code lowers VAT and commission with the price; the list price is kept on the line.
    /// </summary>
    public static Purchase Complete(CheckoutSession checkout, Guid customerId, string email, PaidAmount paid, string? paymentIntentId, DateTimeOffset now)
    {
        var listGross = checkout.UnitPrice * checkout.Quantity;
        var lineGross = paid.Total; // one line per checkout: it carries the whole payment
        var lineVat = Vat.Included(lineGross, checkout.VatRatePercent);
        var lineNet = lineGross - lineVat;
        var discount = Math.Max(0, listGross - lineGross);
        var purchase = new Purchase
        {
            Id = Guid.CreateVersion7(now),
            CheckoutSessionId = checkout.Id,
            CustomerId = customerId,
            AmountTotal = paid.Total,
            VatAmount = lineVat,
            DiscountAmount = discount,
            PromotionCode = paid.PromotionCode,
            Currency = checkout.Currency,
            ProviderPaymentIntentId = paymentIntentId,
            CompletedAt = now,
        };
        purchase.Lines.Add(new PurchaseLine
        {
            Id = Guid.CreateVersion7(now),
            PurchaseId = purchase.Id,
            EventId = checkout.EventId,
            EventTitle = checkout.EventTitle,
            TicketTypeId = checkout.TicketTypeId,
            TicketTypeName = checkout.TicketTypeName,
            Quantity = checkout.Quantity,
            UnitPrice = checkout.UnitPrice,
            VatRatePercent = checkout.VatRatePercent,
            IncludesLive = checkout.IncludesLive,
            ReplayAccessDays = checkout.ReplayAccessDays,
            GrossAmount = lineGross,
            DiscountAmount = discount,
            NetAmount = lineNet,
            PlatformFeeAmount = Commission.Fee(lineNet, checkout.Quantity, checkout.CommissionPercent, checkout.CommissionFixedAmountPerTicket),
        });

        purchase.Raise(new PurchaseCompleted(
            purchase.Id, customerId, email, now, purchase.AmountTotal, purchase.VatAmount, purchase.Currency,
            purchase.Lines.Select(l => new PurchaseCompletedLine(
                l.Id, l.EventId, l.EventTitle, l.TicketTypeId, l.TicketTypeName, l.Quantity, l.UnitPrice, l.VatRatePercent,
                l.IncludesLive, l.ReplayAccessDays)).ToList(),
            purchase.DiscountAmount,
            purchase.PromotionCode));
        return purchase;
    }

    public bool Refund(DateTimeOffset now)
    {
        if (Status == PurchaseStatus.Refunded)
        {
            return false;
        }

        Status = PurchaseStatus.Refunded;
        RefundedAt = now;
        Raise(new PurchaseRefunded(Id, Lines.Select(l => l.Id).ToList()));
        return true;
    }
}

/// <summary>What the payment provider charged: the total including VAT after discounts, and the promotion code used.</summary>
internal sealed record PaidAmount(long Total, string? PromotionCode = null);

/// <summary>
/// One bought ticket type. Lines (rather than a single ticket on the purchase) allow selling a physical
/// ticket and a stream in the same order later.
/// </summary>
internal sealed class PurchaseLine : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PurchaseId { get; set; }

    public Guid EventId { get; set; }

    public required string EventTitle { get; set; }

    public Guid TicketTypeId { get; set; }

    public required string TicketTypeName { get; set; }

    public int Quantity { get; set; }

    /// <summary>List price per ticket including VAT, in minor units.</summary>
    public long UnitPrice { get; set; }

    public decimal VatRatePercent { get; set; }

    public bool IncludesLive { get; set; }

    public int? ReplayAccessDays { get; set; }

    /// <summary>What was actually paid for the line including VAT (list price minus discount), in minor units.</summary>
    public long GrossAmount { get; set; }

    /// <summary>Discount on the line including VAT (list price × quantity − <see cref="GrossAmount"/>).</summary>
    public long DiscountAmount { get; set; }

    /// <summary>Line amount actually paid excluding VAT, in minor units.</summary>
    public long NetAmount { get; set; }

    /// <summary>Fidoco's commission excluding VAT, computed from the terms at purchase time and never recomputed.</summary>
    public long PlatformFeeAmount { get; set; }
}

internal static class Commission
{
    /// <summary>Percent of the net amount plus a fixed amount per ticket, never more than the net amount.</summary>
    public static long Fee(long netAmount, int quantity, decimal percent, long fixedPerTicket)
    {
        var fee = (long)Math.Round(netAmount * percent / 100m, MidpointRounding.AwayFromZero) + fixedPerTicket * quantity;
        return Math.Clamp(fee, 0, netAmount);
    }
}

internal static class OrderNumbers
{
    /// <summary>The order number shown on receipts and in the settlement: the first 12 hex digits of the purchase id.</summary>
    public static string For(Guid purchaseId) => purchaseId.ToString("N")[..12].ToUpperInvariant();
}

internal static class Vat
{
    /// <summary>The VAT included in a gross amount: 19900 at 25 % → 3980.</summary>
    public static long Included(long grossAmount, decimal ratePercent) =>
        (long)Math.Round(grossAmount * ratePercent / (100m + ratePercent), MidpointRounding.AwayFromZero);
}
