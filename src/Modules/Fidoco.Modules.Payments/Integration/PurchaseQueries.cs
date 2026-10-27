using Fidoco.Contracts.Payments;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Payments.Integration;

internal sealed class PurchaseQueries(PaymentsDbContext db) : IPurchaseQueries
{
    public Task<bool> IsRefundedAsync(Guid purchaseId, CancellationToken cancellationToken) =>
        db.Purchases.AsNoTracking().AnyAsync(p => p.Id == purchaseId && p.Status == PurchaseStatus.Refunded, cancellationToken);

    public async Task<IReadOnlyList<CustomerPurchaseLine>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var lines = await db.Purchases.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .SelectMany(p => p.Lines, (p, l) => new { Purchase = p, Line = l })
            .ToListAsync(cancellationToken);
        return lines
            .OrderByDescending(x => x.Purchase.CompletedAt)
            .Select(x => new CustomerPurchaseLine(
                x.Purchase.Id, x.Line.Id, OrderNumbers.For(x.Purchase.Id), x.Line.EventId, x.Line.EventTitle, x.Line.TicketTypeName,
                x.Line.Quantity, x.Line.GrossAmount, x.Line.DiscountAmount, x.Purchase.PromotionCode, x.Purchase.Currency, x.Purchase.CompletedAt,
                x.Purchase.Status == PurchaseStatus.Refunded))
            .ToList();
    }
}
