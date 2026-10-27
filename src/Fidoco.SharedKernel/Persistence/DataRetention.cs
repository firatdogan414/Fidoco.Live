using Microsoft.EntityFrameworkCore;

namespace Fidoco.SharedKernel.Persistence;

/// <summary>
/// Deletes data that is no longer needed (GDPR: keep only what is necessary). Run daily by the host.
/// Retention tasks work across tenants on purpose and use set-based deletes.
/// </summary>
public interface IDataRetentionTask
{
    Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Processed outbox rows are kept 30 days for troubleshooting, then deleted.</summary>
public sealed class OutboxRetention<TContext>(TContext db) : IDataRetentionTask
    where TContext : ModuleDbContext
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - KeepFor;
        return db.OutboxMessages.Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
