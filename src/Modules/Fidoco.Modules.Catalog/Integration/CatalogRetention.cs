using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Catalog.Integration;

/// <summary>Mux webhook dedupe rows are deleted after 30 days.</summary>
internal sealed class CatalogRetention(CatalogDbContext db) : IDataRetentionTask
{
    public Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-30);
        return db.ProcessedWebhookEvents.Where(w => w.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
