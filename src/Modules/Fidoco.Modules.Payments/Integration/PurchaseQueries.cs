using Fidoco.Contracts.Payments;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Payments.Integration;

internal sealed class PurchaseQueries(PaymentsDbContext db) : IPurchaseQueries
{
    public Task<bool> IsRefundedAsync(Guid purchaseId, CancellationToken cancellationToken) =>
        db.Purchases.AsNoTracking().AnyAsync(p => p.Id == purchaseId && p.Status == PurchaseStatus.Refunded, cancellationToken);
}
