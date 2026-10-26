using Fidoco.Modules.Catalog.Domain;
using Fidoco.SharedKernel.Persistence;
using Fidoco.SharedKernel.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Catalog.Infrastructure;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Catalog";

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventStream> Streams => Set<EventStream>();

    public DbSet<StreamRecording> StreamRecordings => Set<StreamRecording>();

    public DbSet<TicketType> TicketTypes => Set<TicketType>();

    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(e =>
        {
            e.ToTable("Events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.StartsAt });
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(5000);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Streams).WithOne().HasForeignKey(s => s.EventId);
            e.HasMany(x => x.TicketTypes).WithOne().HasForeignKey(t => t.EventId);
        });

        modelBuilder.Entity<EventStream>(e =>
        {
            e.ToTable("Streams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.ProviderStreamId).HasMaxLength(100);
            e.HasIndex(x => x.ProviderStreamId).IsUnique();
            e.Property(x => x.LivePlaybackId).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LatencyMode).HasMaxLength(20);
            e.HasMany(x => x.Recordings).WithOne().HasForeignKey(r => r.StreamId);
        });

        modelBuilder.Entity<StreamRecording>(e =>
        {
            e.ToTable("StreamRecordings");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderAssetId).HasMaxLength(100);
            e.HasIndex(x => x.ProviderAssetId).IsUnique();
            e.Property(x => x.PlaybackId).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<TicketType>(e =>
        {
            e.ToTable("TicketTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.AccessMode).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
        });

        modelBuilder.ConfigureProcessedWebhookEvents("CatalogProcessedWebhooks");
    }
}

internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<CatalogDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
