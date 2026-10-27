using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Catalog.Domain;

internal enum StreamStatus
{
    Provisioning,
    Idle,
    Active,
    Disabled,
}

/// <summary>A live video feed of an event (one in phase 1; parallel stages later). Backed by a Mux live stream.</summary>
internal sealed class EventStream : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EventId { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public string Provider { get; set; } = "Mux";

    /// <summary>The provider's live stream id. The stream key is never stored; it is fetched on demand.</summary>
    public string? ProviderStreamId { get; set; }

    /// <summary>Signed playback id for live viewing.</summary>
    public string? LivePlaybackId { get; set; }

    public StreamStatus Status { get; set; } = StreamStatus.Provisioning;

    public DateTimeOffset? StatusChangedAt { get; set; }

    public required string LatencyMode { get; set; }

    public int ReconnectWindowSeconds { get; set; }

    public bool PrimaryRecordingChosenByAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<StreamRecording> Recordings { get; set; } = [];

    public void SelectLongestRecordingAsPrimary()
    {
        var longest = Recordings
            .Where(r => r.Status == RecordingStatus.Ready)
            .OrderByDescending(r => r.DurationSeconds ?? 0)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault();
        foreach (var recording in Recordings)
        {
            recording.IsPrimary = recording == longest;
        }
    }

    public bool SelectPrimaryRecording(Guid recordingId)
    {
        var chosen = Recordings.SingleOrDefault(r => r.Id == recordingId && r.Status == RecordingStatus.Ready);
        if (chosen is null)
        {
            return false;
        }

        foreach (var recording in Recordings)
        {
            recording.IsPrimary = recording == chosen;
        }

        PrimaryRecordingChosenByAdmin = true;
        return true;
    }
}

internal enum RecordingStatus
{
    Preparing,
    Ready,
    Errored,
}

/// <summary>
/// A recording (Mux asset) of a stream, used for replay. A stream gets several if the encoder was
/// disconnected for longer than the reconnect window.
/// </summary>
internal sealed class StreamRecording : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid StreamId { get; set; }

    public required string ProviderAssetId { get; set; }

    /// <summary>Signed playback id for replay.</summary>
    public string? PlaybackId { get; set; }

    public double? DurationSeconds { get; set; }

    public RecordingStatus Status { get; set; } = RecordingStatus.Preparing;

    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
