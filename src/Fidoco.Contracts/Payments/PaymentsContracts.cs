using Fidoco.SharedKernel.Domain;

namespace Fidoco.Contracts.Payments;

/// <summary>A bought ticket line, with the access terms that were sold (snapshot at purchase time).</summary>
public sealed record PurchaseCompletedLine(
    Guid PurchaseLineId,
    Guid EventId,
    string EventTitle,
    Guid TicketTypeId,
    string TicketTypeName,
    int Quantity,
    long UnitPrice,
    decimal VatRatePercent,
    bool IncludesLive,
    int? ReplayAccessDays);

/// <summary>
/// Raised when a payment is confirmed. Playback grants access; Notifications sends the receipt.
/// <see cref="AmountTotal"/> and <see cref="VatAmount"/> are what was actually paid; lines carry list prices and
/// <see cref="DiscountAmount"/> is the difference (from <see cref="PromotionCode"/>).
/// </summary>
public sealed record PurchaseCompleted(
    Guid PurchaseId,
    Guid CustomerId,
    string Email,
    DateTimeOffset CompletedAt,
    long AmountTotal,
    long VatAmount,
    string Currency,
    IReadOnlyList<PurchaseCompletedLine> Lines,
    long DiscountAmount = 0,
    string? PromotionCode = null) : IDomainEvent;

/// <summary>Raised when a purchase is fully refunded; access is revoked.</summary>
public sealed record PurchaseRefunded(Guid PurchaseId, IReadOnlyList<Guid> PurchaseLineIds) : IDomainEvent;

/// <summary>One bought ticket line as support sees it; <see cref="AmountPaid"/> is what the buyer actually paid.</summary>
public sealed record CustomerPurchaseLine(
    Guid PurchaseId,
    Guid PurchaseLineId,
    string OrderNumber,
    Guid EventId,
    string EventTitle,
    string TicketTypeName,
    int Quantity,
    long AmountPaid,
    long DiscountAmount,
    string? PromotionCode,
    string Currency,
    DateTimeOffset CompletedAt,
    bool Refunded);

/// <summary>Purchase lookups for other modules (current tenant).</summary>
public interface IPurchaseQueries
{
    /// <summary>True when the purchase has been refunded.</summary>
    Task<bool> IsRefundedAsync(Guid purchaseId, CancellationToken cancellationToken);

    /// <summary>The customer's purchases, newest first.</summary>
    Task<IReadOnlyList<CustomerPurchaseLine>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}
