using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Catalog.Infrastructure.Video;

internal sealed class MuxOptions
{
    public const string SectionName = "Mux";

    /// <summary>Mux API access token id (Settings → Access Tokens; needs Mux Video read/write).</summary>
    public string TokenId { get; set; } = "";

    public string TokenSecret { get; set; } = "";

    /// <summary>Signing secret of the Mux webhook endpoint.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>"standard", "reduced" or "low".</summary>
    public string LatencyMode { get; set; } = "standard";

    /// <summary>How long Mux waits for the encoder to reconnect before the stream goes idle (and the event ends).</summary>
    public int ReconnectWindowSeconds { get; set; } = 300;
}

/// <summary>Mux Video REST API (https://docs.mux.com/api-reference) via HttpClient.</summary>
internal sealed class MuxVideoProvider(HttpClient http, IOptions<MuxOptions> options) : IVideoProvider
{
    public const string ApiBaseAddress = "https://api.mux.com/";

    public string RtmpUrl => "rtmp://global-live.mux.com:5222/app";

    public string RtmpsUrl => "rtmps://global-live.mux.com:443/app";

    public async Task<CreatedLiveStream> CreateLiveStreamAsync(LiveStreamSettings settings, CancellationToken cancellationToken)
    {
        var body = new
        {
            playback_policies = new[] { "signed" },
            new_asset_settings = new { playback_policies = new[] { "signed" } },
            latency_mode = settings.LatencyMode,
            reconnect_window = settings.ReconnectWindowSeconds,
            passthrough = settings.Passthrough,
        };
        using var request = Request(HttpMethod.Post, "video/v1/live-streams");
        request.Content = JsonContent.Create(body);
        var stream = await SendAsync(request, cancellationToken);

        // Fail closed: never accept a public playback id for paid content.
        var signed = stream.PlaybackIds?.FirstOrDefault(p => p.Policy == "signed")
            ?? throw new InvalidOperationException($"Mux live stream {stream.Id} has no signed playback id.");
        return new CreatedLiveStream(stream.Id, signed.Id);
    }

    public async Task<LiveStreamDetails> GetLiveStreamAsync(string providerStreamId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"video/v1/live-streams/{Uri.EscapeDataString(providerStreamId)}");
        var stream = await SendAsync(request, cancellationToken);
        return new LiveStreamDetails(stream.Id, stream.Status ?? "idle", stream.StreamKey ?? "");
    }

    public async Task<string> ResetStreamKeyAsync(string providerStreamId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"video/v1/live-streams/{Uri.EscapeDataString(providerStreamId)}/reset-stream-key");
        var stream = await SendAsync(request, cancellationToken);
        return stream.StreamKey ?? throw new InvalidOperationException("Mux returned no stream key.");
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var settings = options.Value;
        var request = new HttpRequestMessage(method, path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.TokenId}:{settings.TokenSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private async Task<MuxLiveStream> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<MuxEnvelope>(cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("Empty response from Mux.");
    }

    private sealed record MuxEnvelope([property: JsonPropertyName("data")] MuxLiveStream? Data);

    private sealed record MuxLiveStream(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("stream_key")] string? StreamKey,
        [property: JsonPropertyName("playback_ids")] List<MuxPlaybackId>? PlaybackIds);

    private sealed record MuxPlaybackId(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("policy")] string Policy);
}
