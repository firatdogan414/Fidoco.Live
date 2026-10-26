using Fidoco.Contracts.Identity;
using Fidoco.Modules.Payments.Domain;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Payments.Features;

/// <summary>
/// GDPR: purchases are bookkeeping records and must be kept for 7 years, so they are anonymized
/// (the link to the customer is removed) instead of deleted. Payments never stores the e-mail address.
/// </summary>
internal sealed class AnonymizePurchasesOnCustomerDeleted(PaymentsDbContext db) : IDomainEventHandler<CustomerDeleted>
{
    public async Task HandleAsync(CustomerDeleted domainEvent, CancellationToken cancellationToken)
    {
        var purchases = await db.Purchases.Where(p => p.CustomerId == domainEvent.CustomerId).ToListAsync(cancellationToken);
        purchases.ForEach(p => p.CustomerId = null);
        var checkouts = await db.CheckoutSessions.Where(c => c.CustomerId == domainEvent.CustomerId).ToListAsync(cancellationToken);
        checkouts.ForEach(c => c.CustomerId = null);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Abandoned checkouts and webhook dedupe rows are deleted after 30 days.</summary>
internal sealed class PaymentsRetention(PaymentsDbContext db) : IDataRetentionTask
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-30);
        var checkouts = await db.CheckoutSessions.IgnoreQueryFilters()
            .Where(c => c.Status != CheckoutStatus.Completed && c.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var webhooks = await db.ProcessedWebhookEvents.Where(w => w.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        return checkouts + webhooks;
    }
}
