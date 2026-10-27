using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Integration;

/// <summary>
/// Erases a customer (GDPR): deletes the account and its login links and raises CustomerDeleted in
/// the same transaction, so Payments and Playback follow via the outbox.
/// </summary>
internal sealed class CustomerErasure(IdentityDbContext db)
{
    public async Task<bool> EraseAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        customer.MarkDeleted();
        db.LoginTokens.RemoveRange(await db.LoginTokens.Where(t => t.CustomerId == customerId).ToListAsync(cancellationToken));
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>Expired login links and admin password links are deleted a day after they expire.</summary>
internal sealed class IdentityRetention(IdentityDbContext db) : IDataRetentionTask
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-1);
        var loginTokens = await db.LoginTokens.IgnoreQueryFilters().Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        var adminTokens = await db.AdminTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        return loginTokens + adminTokens;
    }
}
