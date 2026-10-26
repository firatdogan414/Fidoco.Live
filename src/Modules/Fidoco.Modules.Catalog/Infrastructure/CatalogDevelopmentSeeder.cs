using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.SharedKernel.Hosting;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Catalog.Infrastructure;

/// <summary>Seeds a published demo event (199 kr, 7 days replay) for the development tenant.</summary>
internal sealed class CatalogDevelopmentSeeder : IDevelopmentSeeder
{
    public int Order => 20;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = await services.GetRequiredService<ITenantDirectory>().FindByHostAsync(DevelopmentSeed.TenantHost, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        using var scope = services.GetRequiredService<ITenantContext>().Use(tenant.Id);
        var db = services.GetRequiredService<CatalogDbContext>();
        if (await db.Events.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var ev = new Event
        {
            Id = Guid.CreateVersion7(now),
            Slug = "afn-12",
            Title = "Apex Fight Night 12",
            Description = "Kvällens huvudmatch plus fem matcher på förkortet. Sänds live i HD.",
            StartsAt = now.AddDays(7).Date.AddHours(18),
            CreatedAt = now,
            UpdatedAt = now,
        };
        ev.TicketTypes.Add(new TicketType { Id = Guid.CreateVersion7(now), EventId = ev.Id, Name = "Livestream", PriceAmount = 19900, ReplayAccessDays = 7 });
        ev.Publish();
        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);
    }
}
