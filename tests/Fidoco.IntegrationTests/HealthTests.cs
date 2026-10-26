using System.Net;
using System.Text.Json;
using Fidoco.IntegrationTests.Infrastructure;
using Fidoco.Modules.Tenants.Infrastructure;
using Fidoco.SharedKernel.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests;

public sealed class HealthTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    [Fact]
    public async Task Liveness_endpoint_returns_healthy()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_reports_databases_outbox_and_external_services()
    {
        var checks = await ChecksAsync(factory);

        foreach (var module in new[] { "Tenants", "Identity", "Catalog", "Payments", "Playback" })
        {
            Assert.Equal("Healthy", checks[$"db-{module}DbContext"]);
        }

        Assert.Equal("Healthy", checks["outbox"]);
        Assert.Equal("Healthy", checks["mux"]); // fake video provider in tests
        Assert.Equal("Degraded", checks["stripe"]); // no Stripe key in tests: degraded, never unhealthy
    }

    [Fact]
    public async Task Failed_outbox_messages_make_health_degraded()
    {
        await using var isolated = new FidocoApiFactory();
        using (var scope = isolated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantsDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "Test",
                HandlerType = "Test",
                Payload = "{}",
                OccurredAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow,
                FailedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var checks = await ChecksAsync(isolated);

        Assert.Equal("Degraded", checks["outbox"]);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404()
    {
        var tenant = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(tenant.Host).GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Dictionary<string, string>> ChecksAsync(FidocoApiFactory app)
    {
        var response = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // Degraded still answers 200
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("checks").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetProperty("status").GetString()!);
    }
}
