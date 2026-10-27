using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

/// <summary>
/// Applies all migrations to an empty SQL Server database and runs the main flow end to end
/// (event → purchase webhook → outbox → entitlement → playback token), catching provider differences
/// the SQLite tests cannot see.
/// </summary>
[Trait("Category", "SqlServer")]
public sealed class SqlServerSmokeTests : IDisposable
{
    private readonly SqlServerApiFactory _factory = new();

    [SqlServerFact]
    public async Task Purchase_to_playback_works_on_migrated_sql_server()
    {
        var (e, viewer, email) = await _factory.LiveEventWithBuyerAsync();

        var tenant = await viewer.GetFromJsonAsync<Dictionary<string, object>>("/api/v1/tenant");
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var heartbeat = await viewer.HeartbeatAsync(session.SessionId);
        var sales = await e.Admin.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/admin/events/{e.Event.Id}/sales");

        Assert.Equal("tenant", tenant!["kind"].ToString());
        Assert.Equal("live", session.Mode);
        Assert.True(heartbeat.IsSuccessStatusCode);
        Assert.Equal("1", sales!["ticketsSold"].ToString());
        Assert.Single(_factory.Emails.To(email));
    }

    [SqlServerFact]
    public async Task Support_lookup_and_comps_work_on_migrated_sql_server()
    {
        var (e, _, email) = await _factory.LiveEventWithBuyerAsync();

        var found = await e.Admin.GetFromJsonAsync<List<Dictionary<string, object>>>($"/api/v1/admin/support/customers?email={email[..10].ToUpperInvariant()}");
        var details = await e.Admin.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/admin/support/customers/{found![0]["id"]}");
        var grant = await e.Admin.PostAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}/comps", new { email = "comp@example.com", ticketTypeId = e.TicketTypeId, note = "Press" });
        await _factory.DrainOutboxAsync();
        var settlement = await e.Admin.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/admin/events/{e.Event.Id}/settlement");
        await e.Admin.PostAsJsonAsync("/api/v1/admin/customers/delete", new { email }).EnsureSuccessAsync();
        await _factory.DrainOutboxAsync();
        var log = await e.Admin.GetStringAsync("/api/v1/admin/support/log");

        Assert.Equal(email, found[0]["email"].ToString());
        Assert.Contains("\"accessMode\":\"live\"", details!["tickets"].ToString());
        grant.EnsureSuccessStatusCode();
        Assert.Single(_factory.Emails.To("comp@example.com"));
        Assert.Equal("1", settlement!["complimentaryTickets"].ToString());
        Assert.DoesNotContain(email, log);
    }

    public void Dispose() => _factory.Dispose();
}
