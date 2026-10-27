using System.Net.Http.Json;

namespace Fidoco.IntegrationTests.Infrastructure;

public sealed record PlaybackTokensDto(string Playback, string Thumbnail, string Storyboard);

public sealed record PlaybackSessionDto(string SessionId, string Mode, string PlaybackId, PlaybackTokensDto Tokens, DateTimeOffset TokenExpiresAt, int HeartbeatSeconds);

public sealed record AccessDto(bool HasTicket, string? Mode, string? Reason);

internal static class TestPlayback
{
    /// <summary>A published event that is live (stream active) and a signed-in buyer with a ticket.</summary>
    public static async Task<(TestEvent Event, HttpClient Viewer, string Email)> LiveEventWithBuyerAsync(this FidocoApiFactory factory)
    {
        var e = await factory.CreatePublishedEventAsync();
        var viewer = factory.ClientFor(e.Tenant.Host);
        var email = $"viewer-{Guid.NewGuid():N}@example.com";
        await factory.BuyAsync(viewer, e.TicketTypeId, email);
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        return (e, viewer, email);
    }

    public static Task<HttpResponseMessage> StartSessionAsync(this HttpClient viewer, Guid eventId) =>
        viewer.PostAsync($"/api/v1/events/{eventId}/playback-sessions", null);

    public static async Task<PlaybackSessionDto> StartSessionOkAsync(this HttpClient viewer, Guid eventId)
    {
        var response = await viewer.StartSessionAsync(eventId).EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<PlaybackSessionDto>())!;
    }

    public static Task<HttpResponseMessage> HeartbeatAsync(this HttpClient viewer, string sessionId) =>
        viewer.PostAsync($"/api/v1/playback-sessions/{sessionId}/heartbeat", null);
}
