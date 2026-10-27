using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class TenantResolutionTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Branding(string Name, string Slug, string PrimaryColor, string? SupportEmail);

    private sealed record TenantResponse(string Kind, Branding? Tenant);

    [Fact]
    public async Task Tenant_is_resolved_from_host_header()
    {
        var a = await factory.CreateTenantAsync(name: "Arrangör A");
        var b = await factory.CreateTenantAsync(name: "Arrangör B");

        var fromA = await factory.ClientFor(a.Host).GetFromJsonAsync<TenantResponse>("/api/v1/tenant");
        var fromB = await factory.ClientFor(b.Host).GetFromJsonAsync<TenantResponse>("/api/v1/tenant");

        Assert.Equal("tenant", fromA!.Kind);
        Assert.Equal("Arrangör A", fromA.Tenant!.Name);
        Assert.Equal("Arrangör B", fromB!.Tenant!.Name);
    }

    [Fact]
    public async Task Host_lookup_ignores_case_and_port()
    {
        var a = await factory.CreateTenantAsync(name: "Case");

        var client = factory.ClientFor(a.Host.ToUpperInvariant() + ":8443");
        var response = await client.GetFromJsonAsync<TenantResponse>("/api/v1/tenant");

        Assert.Equal("Case", response!.Tenant!.Name);
    }

    [Fact]
    public async Task Unknown_host_gets_404_with_code_for_api_calls()
    {
        var response = await factory.ClientFor("unknown.test").GetAsync("/api/v1/tenant");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("unknown_tenant", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Platform_host_has_no_tenant()
    {
        var response = await factory.ClientFor(FidocoApiFactory.PlatformHost).GetFromJsonAsync<TenantResponse>("/api/v1/tenant");

        Assert.Equal("platform", response!.Kind);
        Assert.Null(response.Tenant);
    }
}
