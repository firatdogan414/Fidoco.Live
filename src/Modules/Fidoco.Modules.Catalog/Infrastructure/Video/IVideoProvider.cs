namespace Fidoco.Modules.Catalog.Infrastructure.Video;

internal sealed record LiveStreamSettings(string LatencyMode, int ReconnectWindowSeconds, string Passthrough);

internal sealed record CreatedLiveStream(string ProviderStreamId, string PlaybackId);

/// <summary>Provider status: "idle", "active" or "disabled".</summary>
internal sealed record LiveStreamDetails(string ProviderStreamId, string Status, string StreamKey);

/// <summary>Thin abstraction over the video platform (Mux). Live streams always use signed playback.</summary>
internal interface IVideoProvider
{
    /// <summary>RTMP ingest URL for production software (vMix, OBS).</summary>
    string RtmpUrl { get; }

    /// <summary>RTMPS (TLS) ingest URL; preferred when the software supports it.</summary>
    string RtmpsUrl { get; }

    Task<CreatedLiveStream> CreateLiveStreamAsync(LiveStreamSettings settings, CancellationToken cancellationToken);

    Task<LiveStreamDetails> GetLiveStreamAsync(string providerStreamId, CancellationToken cancellationToken);

    /// <summary>Invalidates the current stream key and returns a new one.</summary>
    Task<string> ResetStreamKeyAsync(string providerStreamId, CancellationToken cancellationToken);
}

internal sealed class VideoOptions
{
    public const string SectionName = "Video";

    /// <summary>"Mux" (default) or "Fake" (Development/Testing only).</summary>
    public string Provider { get; set; } = "Mux";
}
