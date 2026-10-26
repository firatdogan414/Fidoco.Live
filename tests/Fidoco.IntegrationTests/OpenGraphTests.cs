using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class OpenGraphTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    [Fact]
    public async Task Event_page_gets_open_graph_tags_from_the_event()
    {
        var e = await factory.CreatePublishedEventAsync(input: TestCatalog.EventInput(title: "Gala & Glamour"));
        await e.Admin.PutAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}", new
        {
            title = "Gala & Glamour",
            description = "Sex matcher\nlive från Göteborg",
            imageUrl = "/files/abc/poster.jpg",
            startsAt = DateTimeOffset.UtcNow.AddDays(3),
            ticket = new { name = "Livestream", priceAmount = 19900, replayAccessDays = 7 },
        }).EnsureSuccessAsync();

        var html = await factory.ClientFor(e.Tenant.Host).GetStringAsync($"/e/{e.Event.Slug}");

        Assert.Contains($"<title>Gala &amp; Glamour – {e.Tenant.Name}</title>", html);
        Assert.Contains("<meta property=\"og:description\" content=\"Sex matcher live från Göteborg\">", html);
        Assert.Contains($"<meta property=\"og:image\" content=\"https://{e.Tenant.Host}/files/abc/poster.jpg\">", html);
        Assert.Contains($"<meta property=\"og:url\" content=\"https://{e.Tenant.Host}/e/{e.Event.Slug}\">", html);
        Assert.Contains("<div id=\"root\"></div>", html);
    }

    [Fact]
    public async Task Other_pages_and_drafts_get_the_tenant_name()
    {
        var tenant = await factory.CreateTenantAsync(name: "Arrangören");
        var admin = await factory.AdminClientAsync(tenant);
        var draft = await admin.CreateEventAsync(TestCatalog.EventInput(title: "Hemligt utkast"));

        var home = await factory.ClientFor(tenant.Host).GetStringAsync("/");
        var draftPage = await factory.ClientFor(tenant.Host).GetStringAsync($"/e/{draft.Slug}");

        Assert.Contains("<meta property=\"og:title\" content=\"Arrangören\">", home);
        Assert.DoesNotContain("Hemligt utkast", draftPage);
    }
}
