using Fidoco.Modules.Tenants.Domain;
using Fidoco.Modules.Tenants.Infrastructure;
using Fidoco.SharedKernel.Outbox;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests.Infrastructure;

public sealed record TestTenant(Guid Id, string Host, string Name);

/// <summary>Helpers for arranging data and calling the API as a given host.</summary>
public static class TestApp
{
    /// <summary>A client for a tenant host with cookies and the CSRF header the SPA always sends.</summary>
    public static HttpClient ClientFor(this WebApplicationFactory<Program> factory, string host)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{host}"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Fidoco-Client", "web");
        return client;
    }

    public static async Task<TestTenant> CreateTenantAsync(this WebApplicationFactory<Program> factory, string? host = null, string? name = null)
    {
        host ??= $"t{Guid.NewGuid():N}"[..12] + ".test";
        name ??= $"Tenant {host}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantsDbContext>();
        var id = Guid.CreateVersion7();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = name,
            Slug = host.Split('.')[0],
            SupportEmail = $"support@{host}",
            CreatedAt = DateTimeOffset.UtcNow,
            Domains = [new TenantDomain { Id = Guid.CreateVersion7(), TenantId = id, Host = host, IsPrimary = true }],
        });
        await db.SaveChangesAsync();
        return new TestTenant(id, host, name);
    }

    /// <summary>Runs every module's outbox until nothing is due (side effects such as entitlements and emails).</summary>
    public static async Task DrainOutboxAsync(this WebApplicationFactory<Program> factory)
    {
        var dispatchers = factory.Services.GetServices<IOutboxDispatcher>().ToList();
        for (var round = 0; round < 10; round++)
        {
            var handled = 0;
            foreach (var dispatcher in dispatchers)
            {
                handled += await dispatcher.ProcessPendingAsync(CancellationToken.None);
            }

            if (handled == 0)
            {
                return;
            }
        }
    }
}
