using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class CatalogTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record PublicTicket(Guid Id, string Name, long PriceAmount, bool OnSale);

    private sealed record PublicEvent(Guid Id, string Slug, string Title, string Status, List<PublicTicket> TicketTypes);

    private sealed record PublicStatus(string Status, bool StreamActive, bool ReplayAvailable);

    private sealed record StreamStatus(string Status, string Source);

    [Fact]
    public async Task Draft_is_hidden_until_published()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);
        var ev = await admin.CreateEventAsync(TestCatalog.EventInput(title: "Apex Fight Night 12"));
        var viewer = factory.ClientFor(tenant.Host);

        Assert.Equal("apex-fight-night-12", ev.Slug);
        Assert.Equal("draft", ev.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/v1/events/{ev.Slug}")).StatusCode);
        Assert.Empty((await viewer.GetFromJsonAsync<List<PublicEvent>>("/api/v1/events"))!);

        (await admin.PostAsync($"/api/v1/admin/events/{ev.Id}/publish", null)).EnsureSuccessStatusCode();

        var published = await viewer.GetFromJsonAsync<PublicEvent>($"/api/v1/events/{ev.Slug}");
        Assert.Equal("scheduled", published!.Status);
        var ticket = Assert.Single(published.TicketTypes);
        Assert.Equal(19900, ticket.PriceAmount);
        Assert.True(ticket.OnSale);
    }

    [Fact]
    public async Task Duplicate_titles_get_unique_slugs()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);

        var first = await admin.CreateEventAsync(TestCatalog.EventInput(title: "Gala"));
        var second = await admin.CreateEventAsync(TestCatalog.EventInput(title: "Gala"));

        Assert.Equal("gala", first.Slug);
        Assert.Equal("gala-2", second.Slug);
    }

    [Fact]
    public async Task Invalid_event_input_is_rejected()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);

        var response = await admin.PostAsJsonAsync("/api/v1/admin/events", TestCatalog.EventInput(title: "", price: 100));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_failed", body);
        Assert.Contains("ticket.priceAmount", body);
        Assert.Contains("title", body);
    }

    [Fact]
    public async Task Admin_endpoints_require_admin_sign_in()
    {
        var tenant = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(tenant.Host).GetAsync("/api/v1/admin/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_see_or_change_another_tenants_event()
    {
        var a = await factory.CreatePublishedEventAsync();
        var b = await factory.CreatePublishedEventAsync();

        var get = await a.Admin.GetAsync($"/api/v1/admin/events/{b.Event.Id}");
        var put = await a.Admin.PutAsJsonAsync($"/api/v1/admin/events/{b.Event.Id}", TestCatalog.EventInput(title: "Hijacked"));
        var stream = await a.Admin.GetAsync($"/api/v1/admin/streams/{b.Event.Stream!.Id}");
        var list = await a.Admin.GetFromJsonAsync<List<AdminEvent>>("/api/v1/admin/events");
        var publicOnA = await factory.ClientFor(a.Tenant.Host).GetAsync($"/api/v1/events/{b.Event.Slug}-does-not-matter");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stream.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, publicOnA.StatusCode);
        Assert.Equal([a.Event.Id], list!.Select(e => e.Id));
    }

    [Fact]
    public async Task Public_event_from_another_tenant_is_not_found_by_slug()
    {
        var a = await factory.CreatePublishedEventAsync(input: TestCatalog.EventInput(title: "Unik gala A"));
        var b = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(b.Host).GetAsync($"/api/v1/events/{a.Event.Slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stream_shows_rtmp_url_and_stream_key_and_only_one_can_be_created()
    {
        var e = await factory.CreatePublishedEventAsync();

        var details = await e.Admin.GetFromJsonAsync<StreamDetails>($"/api/v1/admin/streams/{e.Event.Stream!.Id}");
        var second = await e.Admin.PostAsync($"/api/v1/admin/events/{e.Event.Id}/streams", null);
        var reset = await e.Admin.PostAsync($"/api/v1/admin/streams/{e.Event.Stream.Id}/reset-key", null);
        var newKey = (await reset.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["streamKey"];

        Assert.StartsWith("rtmp://", details!.RtmpUrl);
        Assert.StartsWith("rtmps://", details.RtmpsUrl);
        Assert.False(string.IsNullOrEmpty(details.StreamKey));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.NotEqual(details.StreamKey, newKey);
    }

    [Fact]
    public async Task Status_polling_reports_provider_status_and_reconciles_a_missed_webhook()
    {
        var e = await factory.CreatePublishedEventAsync();
        factory.Video().SetStatus(e.ProviderStreamId, "active");

        var status = await e.Admin.GetFromJsonAsync<StreamStatus>($"/api/v1/admin/streams/{e.Event.Stream!.Id}/status");
        var ev = await e.Admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{e.Event.Id}");

        Assert.Equal("active", status!.Status);
        Assert.Equal("live", ev!.Status);
    }

    [Fact]
    public async Task Mux_webhooks_drive_event_status_and_replay()
    {
        var e = await factory.CreatePublishedEventAsync();
        var t0 = factory.Time.GetUtcNow();

        (await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, t0)).EnsureSuccessStatusCode();
        await factory.PostMuxWebhookAsync("video.asset.created", "asset-" + Guid.NewGuid(), t0, liveStreamId: e.ProviderStreamId, playbackId: "replay-pb");
        var live = await AdminStatus(e);

        (await factory.PostMuxWebhookAsync("video.live_stream.idle", e.ProviderStreamId, t0.AddHours(3))).EnsureSuccessStatusCode();
        var ended = await AdminStatus(e);

        await factory.PostMuxWebhookAsync("video.asset.ready", "asset-" + Guid.NewGuid(), t0.AddHours(3), liveStreamId: e.ProviderStreamId, playbackId: "replay-pb2", duration: 10800);
        // The public status endpoint is cached for a few seconds; this is its first read.
        var publicStatus = await factory.ClientFor(e.Tenant.Host).GetFromJsonAsync<PublicStatus>($"/api/v1/events/{e.Event.Slug}/status");

        Assert.Equal(("live", "active"), live);
        Assert.Equal(("ended", "idle"), ended);
        Assert.Equal("ended", publicStatus!.Status);
        Assert.False(publicStatus.StreamActive);
        Assert.True(publicStatus.ReplayAvailable);
    }

    private static async Task<(string Event, string Stream)> AdminStatus(TestEvent e)
    {
        var ev = (await e.Admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{e.Event.Id}"))!;
        return (ev.Status, ev.Stream!.Status);
    }

    [Fact]
    public async Task Mux_webhook_with_invalid_signature_is_rejected_without_effect()
    {
        var e = await factory.CreatePublishedEventAsync();

        var response = await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow(), secret: "wrong");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var ev = await e.Admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{e.Event.Id}");
        Assert.Equal("scheduled", ev!.Status);
    }

    [Fact]
    public async Task Mux_webhooks_are_idempotent_and_ignore_out_of_order_delivery()
    {
        var e = await factory.CreatePublishedEventAsync();
        var t0 = factory.Time.GetUtcNow();
        var activeId = Guid.NewGuid().ToString();

        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, t0, eventId: activeId);
        await factory.PostMuxWebhookAsync("video.live_stream.idle", e.ProviderStreamId, t0.AddHours(2));
        // Redelivery of the first webhook and a late, older "active" must not resurrect the event.
        var redelivered = await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, t0, eventId: activeId);
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, t0.AddHours(1));

        var ev = await e.Admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{e.Event.Id}");
        Assert.Equal(HttpStatusCode.OK, redelivered.StatusCode);
        Assert.Equal("ended", ev!.Status);
    }

    [Fact]
    public async Task First_webhook_applies_even_if_the_providers_clock_is_behind_ours()
    {
        var e = await factory.CreatePublishedEventAsync();

        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow().AddSeconds(-2));

        var ev = await e.Admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{e.Event.Id}");
        Assert.Equal("live", ev!.Status);
    }

    [Fact]
    public async Task Mux_webhook_for_unknown_stream_is_acknowledged()
    {
        var response = await factory.PostMuxWebhookAsync("video.live_stream.active", "unknown-stream", factory.Time.GetUtcNow());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

}
