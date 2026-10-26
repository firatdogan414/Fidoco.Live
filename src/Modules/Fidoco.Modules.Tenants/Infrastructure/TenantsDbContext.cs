using Fidoco.Modules.Tenants.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Tenants.Infrastructure;

internal sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Tenants";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Slug).HasMaxLength(100);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.LogoUrl).HasMaxLength(500);
            e.Property(t => t.PrimaryColor).HasMaxLength(20);
            e.Property(t => t.AccentColor).HasMaxLength(20);
            e.Property(t => t.SupportEmail).HasMaxLength(254);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(t => t.Domains).WithOne().HasForeignKey(d => d.TenantId);
        });

        modelBuilder.Entity<TenantDomain>(e =>
        {
            e.ToTable("TenantDomains");
            e.HasKey(d => d.Id);
            e.Property(d => d.Host).HasMaxLength(253);
            e.HasIndex(d => d.Host).IsUnique();
        });
    }
}

internal sealed class TenantsDbContextFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<TenantsDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
