using Fidoco.Modules.Payments.Domain;
using Fidoco.SharedKernel.Persistence;
using Fidoco.SharedKernel.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.Modules.Payments.Infrastructure;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public override string ModuleName => "Payments";

    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CheckoutSession>(e =>
        {
            e.ToTable("CheckoutSessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderSessionId).HasMaxLength(200);
            e.HasIndex(x => x.ProviderSessionId).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BrowserNonceHash).HasMaxLength(64);
            e.Property(x => x.EventSlug).HasMaxLength(100);
            e.Property(x => x.EventTitle).HasMaxLength(200);
            e.Property(x => x.TicketTypeName).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
            e.Property(x => x.CommissionPercent).HasPrecision(5, 2);
        });

        modelBuilder.Entity<Purchase>(e =>
        {
            e.ToTable("Purchases");
            e.HasKey(x => x.Id);
            // Second line of defense against double processing of the same checkout.
            e.HasIndex(x => x.CheckoutSessionId).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ProviderPaymentIntentId).HasMaxLength(200);
            e.HasIndex(x => x.ProviderPaymentIntentId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseId);
        });

        modelBuilder.Entity<PurchaseLine>(e =>
        {
            e.ToTable("PurchaseLines");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.EventId });
            e.Property(x => x.EventTitle).HasMaxLength(200);
            e.Property(x => x.TicketTypeName).HasMaxLength(100);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
        });

        modelBuilder.ConfigureProcessedWebhookEvents("PaymentsProcessedWebhooks");
    }
}

internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.SqlServerOptions<PaymentsDbContext>(), ModuleDbContextDependencies.ForDesignTime());
}
