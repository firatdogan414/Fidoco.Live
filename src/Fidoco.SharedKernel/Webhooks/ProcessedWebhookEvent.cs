using Microsoft.EntityFrameworkCore;

namespace Fidoco.SharedKernel.Webhooks;

/// <summary>Deduplicates incoming webhooks by provider event id (insert in the same transaction as the effect).</summary>
public sealed class ProcessedWebhookEvent
{
    public required string Provider { get; set; }

    public required string EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

public static class ProcessedWebhookEventModel
{
    public static void ConfigureProcessedWebhookEvents(this ModelBuilder modelBuilder, string tableName)
    {
        modelBuilder.Entity<ProcessedWebhookEvent>(e =>
        {
            e.ToTable(tableName);
            e.HasKey(w => new { w.Provider, w.EventId });
            e.Property(w => w.Provider).HasMaxLength(50);
            e.Property(w => w.EventId).HasMaxLength(200);
        });
    }
}
