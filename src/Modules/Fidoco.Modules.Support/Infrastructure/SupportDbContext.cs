using Fidoco.Modules.Support.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Support.Infrastructure;

/// <summary>The support log. Customers, purchases and tickets stay in their own modules.</summary>
internal sealed class SupportDbContext(DbContextOptions<SupportDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Support";

    public DbSet<SupportLogEntry> Log => Set<SupportLogEntry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupportLogEntry>(e =>
        {
            e.ToTable("SupportLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.AdminEmail).HasMaxLength(254);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.CustomerEmail).HasMaxLength(254);
            e.Property(x => x.EventTitle).HasMaxLength(200);
            e.Property(x => x.Details).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.CustomerId });
        });
    }
}

internal sealed class SupportDbContextFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<SupportDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
