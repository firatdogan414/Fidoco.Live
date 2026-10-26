using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fidoco.Modules.Catalog.Features.Webhooks;
using Fidoco.Modules.Catalog.Infrastructure.Video;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests.Infrastructure;

public sealed record AdminEvent(Guid Id, string Slug, string Title, string Status, AdminTicket? Ticket, AdminStream? Stream);

public sealed record AdminTicket(Guid Id, string Name, long PriceAmount, int? ReplayAccessDays);

public sealed record AdminStream(Guid Id, string Name, string Status);

public sealed record StreamDetails(Guid Id, string Status, string RtmpUrl, string RtmpsUrl, string StreamKey);

/// <summary>A tenant with a signed-in admin and a published event with a stream.</summary>
public sealed record TestEvent(TestTenant Tenant, HttpClient Admin, AdminEvent Event, string ProviderStreamId)
{
    public Guid TicketTypeId => Event.Ticket!.Id;
}

internal static class TestCatalog
{
    public const string AdminPassword = "Admin-pass-123";

    public static async Task<HttpClient> AdminClientAsync(this FidocoApiFactory factory, TestTenant tenant)
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await factory.CreateTenantAdminAsync(tenant, email, AdminPassword);
        var client = factory.ClientFor(tenant.Host);
        await client.SignInAdminAsync(email, AdminPassword);
        return client;
    }

    public static object EventInput(string title = "AFN 12", long price = 19900, int? replayDays = 7, DateTimeOffset? startsAt = null) => new
    {
        title,
        description = "Gala",
        startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(7),
        ticket = new { name = "Livestream", priceAmount = price, replayAccessDays = replayDays },
    };

    public static async Task<AdminEvent> CreateEventAsync(this HttpClient admin, object? input = null)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/events", input ?? EventInput()).EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<AdminEvent>())!;
    }

    /// <summary>Creates a tenant, admin, published event with a (fake) Mux stream.</summary>
    public static async Task<TestEvent> CreatePublishedEventAsync(this FidocoApiFactory factory, TestTenant? tenant = null, object? input = null)
    {
        tenant ??= await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);
        var ev = await admin.CreateEventAsync(input);
        await admin.PostAsync($"/api/v1/admin/events/{ev.Id}/publish", null).EnsureSuccessAsync();
        await admin.PostAsync($"/api/v1/admin/events/{ev.Id}/streams", null).EnsureSuccessAsync();
        var refreshed = (await admin.GetFromJsonAsync<AdminEvent>($"/api/v1/admin/events/{ev.Id}"))!;
        return new TestEvent(tenant, admin, refreshed, await factory.ProviderStreamIdAsync(refreshed.Stream!.Id));
    }

    public static async Task<string> ProviderStreamIdAsync(this FidocoApiFactory factory, Guid streamId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Fidoco.Modules.Catalog.Infrastructure.CatalogDbContext>();
        return System.Linq.Enumerable.Single(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Streams),
            s => s.Id == streamId).ProviderStreamId!;
    }

    public static FakeVideoProvider Video(this FidocoApiFactory factory) => factory.Services.GetRequiredService<FakeVideoProvider>();

    /// <summary>Posts a correctly signed Mux webhook to the platform host.</summary>
    public static Task<HttpResponseMessage> PostMuxWebhookAsync(
        this FidocoApiFactory factory,
        string type,
        string objectId,
        DateTimeOffset createdAt,
        string? liveStreamId = null,
        string? playbackId = null,
        double? duration = null,
        string? eventId = null,
        string? secret = null)
    {
        var data = new Dictionary<string, object?> { ["id"] = objectId };
        if (liveStreamId is not null)
        {
            data["live_stream_id"] = liveStreamId;
        }

        if (playbackId is not null)
        {
            data["playback_ids"] = new[] { new { id = playbackId, policy = "signed" } };
        }

        if (duration is not null)
        {
            data["duration"] = duration;
        }

        var body = JsonSerializer.Serialize(new
        {
            type,
            id = eventId ?? Guid.NewGuid().ToString(),
            created_at = createdAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture),
            data,
        });
        var timestamp = factory.Time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/mux")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Mux-Signature", $"t={timestamp},v1={MuxSignature.Compute(timestamp, body, secret ?? FidocoApiFactory.MuxWebhookSecret)}");
        return factory.ClientFor(FidocoApiFactory.PlatformHost).SendAsync(request);
    }
}
