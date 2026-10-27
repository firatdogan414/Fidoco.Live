using Fidoco.Modules.Playback.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Playback.Infrastructure;

/// <summary>Only entitlements live in the database; playback sessions live in the distributed cache.</summary>
internal sealed class PlaybackDbContext(DbContextOptions<PlaybackDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Playback";

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entitlement>(e =>
        {
            e.ToTable("Entitlements");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.EventId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.GrantedBy).HasMaxLength(254);
            e.HasIndex(x => new { x.TenantId, x.EventId, x.SourceType });
        });
    }
}

internal sealed class PlaybackDbContextFactory : IDesignTimeDbContextFactory<PlaybackDbContext>
{
    public PlaybackDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<PlaybackDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
