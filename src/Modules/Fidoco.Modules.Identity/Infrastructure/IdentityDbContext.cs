using Fidoco.Modules.Identity.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Identity.Infrastructure;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies), IDataProtectionKeyContext
{
    public override string ModuleName => "Identity";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    public DbSet<AdminToken> AdminTokens => Set<AdminToken>();

    /// <summary>Shared key ring so every instance can read auth cookies.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.Id);
            e.Property(c => c.Email).HasMaxLength(254);
            e.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        });

        modelBuilder.Entity<LoginToken>(e =>
        {
            e.ToTable("LoginTokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.CustomerId);
            e.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.ReturnPath).HasMaxLength(500);
        });

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.ToTable("AdminUsers");
            e.HasKey(a => a.Id);
            e.Property(a => a.Email).HasMaxLength(254);
            e.HasIndex(a => new { a.TenantId, a.Email }).IsUnique();
            e.Property(a => a.PasswordHash).HasMaxLength(500);
            e.Property(a => a.SecurityStamp).HasMaxLength(64);
            e.Property(a => a.Role).HasMaxLength(30);
        });

        modelBuilder.Entity<PlatformAdmin>(e =>
        {
            e.ToTable("PlatformAdmins");
            e.HasKey(a => a.Id);
            e.Property(a => a.Email).HasMaxLength(254);
            e.HasIndex(a => a.Email).IsUnique();
            e.Property(a => a.PasswordHash).HasMaxLength(500);
            e.Property(a => a.SecurityStamp).HasMaxLength(64);
        });

        modelBuilder.Entity<AdminToken>(e =>
        {
            e.ToTable("AdminTokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.AdminKind, t.AdminId });
            e.Property(t => t.AdminKind).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<DataProtectionKey>(e => e.ToTable("DataProtectionKeys"));
    }
}

internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<IdentityDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
