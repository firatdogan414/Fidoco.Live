using Fidoco.Modules.Tenants.Domain;
using Fidoco.SharedKernel.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Tenants.Infrastructure;

/// <summary>Seeds Apex Fight Night on afn.localhost for local development.</summary>
internal sealed class TenantsDevelopmentSeeder : IDevelopmentSeeder
{
    public int Order => 0;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TenantsDbContext>();
        if (await db.TenantDomains.AnyAsync(d => d.Host == DevelopmentSeed.TenantHost, cancellationToken))
        {
            return;
        }

        var time = services.GetRequiredService<TimeProvider>();
        var tenantId = Guid.CreateVersion7();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Apex Fight Night",
            Slug = "afn",
            PrimaryColor = "#0f172a",
            AccentColor = "#dc2626",
            SupportEmail = "info@afnmma.se",
            CreatedAt = time.GetUtcNow(),
            Domains = [new TenantDomain { Id = Guid.CreateVersion7(), TenantId = tenantId, Host = DevelopmentSeed.TenantHost, IsPrimary = true }],
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
