using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

/// <summary>
/// Applies all migrations to an empty SQL Server database and exercises the main flow, catching
/// provider differences the SQLite tests cannot see.
/// </summary>
[Trait("Category", "SqlServer")]
public sealed class SqlServerSmokeTests : IDisposable
{
    private readonly SqlServerApiFactory _factory = new();

    [SqlServerFact]
    public async Task App_starts_on_migrated_database_and_resolves_tenant()
    {
        var tenant = await _factory.CreateTenantAsync(name: "SQL Server AB");

        var response = await _factory.ClientFor(tenant.Host).GetFromJsonAsync<Dictionary<string, object>>("/api/v1/tenant");

        Assert.Equal("tenant", response!["kind"].ToString());
    }

    public void Dispose() => _factory.Dispose();
}
