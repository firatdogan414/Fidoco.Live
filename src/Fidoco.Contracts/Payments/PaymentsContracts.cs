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

/// <summary>Raised when a payment is confirmed. Playback grants access; Notifications sends the receipt.</summary>
public sealed record PurchaseCompleted(
    Guid PurchaseId,
    Guid CustomerId,
    string Email,
    DateTimeOffset CompletedAt,
    long AmountTotal,
    long VatAmount,
    string Currency,
    IReadOnlyList<PurchaseCompletedLine> Lines) : IDomainEvent;

/// <summary>Raised when a purchase is fully refunded; access is revoked.</summary>
public sealed record PurchaseRefunded(Guid PurchaseId, IReadOnlyList<Guid> PurchaseLineIds) : IDomainEvent;

/// <summary>Purchase lookups for other modules (current tenant).</summary>
public interface IPurchaseQueries
{
    /// <summary>True when the purchase has been refunded.</summary>
    Task<bool> IsRefundedAsync(Guid purchaseId, CancellationToken cancellationToken);
}
