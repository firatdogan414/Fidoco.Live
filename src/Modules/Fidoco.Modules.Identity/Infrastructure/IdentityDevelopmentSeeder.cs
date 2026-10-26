using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Identity.Domain;
using Fidoco.SharedKernel.Hosting;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Identity.Infrastructure;

/// <summary>Seeds a TenantAdmin for the development tenant (see DevelopmentSeed for credentials).</summary>
internal sealed class IdentityDevelopmentSeeder : IDevelopmentSeeder
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = await services.GetRequiredService<ITenantDirectory>().FindByHostAsync(DevelopmentSeed.TenantHost, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        using var scope = services.GetRequiredService<ITenantContext>().Use(tenant.Id);
        var db = services.GetRequiredService<IdentityDbContext>();
        if (await db.AdminUsers.AnyAsync(a => a.Email == DevelopmentSeed.TenantAdminEmail, cancellationToken))
        {
            return;
        }

        var user = new AdminUser
        {
            Id = Guid.CreateVersion7(),
            Email = DevelopmentSeed.TenantAdminEmail,
            Role = Roles.TenantAdmin,
            CreatedAt = services.GetRequiredService<TimeProvider>().GetUtcNow(),
        };
        user.PasswordHash = services.GetRequiredService<IPasswordHasher<AdminUser>>().HashPassword(user, DevelopmentSeed.TenantAdminPassword);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }
}
