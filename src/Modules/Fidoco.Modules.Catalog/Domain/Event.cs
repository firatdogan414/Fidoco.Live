using Fidoco.Contracts.Catalog;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Tenancy;

namespace Fidoco.Modules.Catalog.Domain;

/// <summary>An event (gala, concert, lecture). Aggregate root for its streams, recordings and ticket types.</summary>
internal sealed class Event : AggregateRoot, ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public EventStatus Status { get; private set; } = EventStatus.Draft;

    public DateTimeOffset? LiveStartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<EventStream> Streams { get; set; } = [];

    public List<TicketType> TicketTypes { get; set; } = [];

    public EventStream? PrimaryStream => Streams.OrderBy(s => s.SortOrder).FirstOrDefault();

    public bool Publish()
    {
        if (Status != EventStatus.Draft || TicketTypes.Count == 0)
        {
            return false;
        }

        Status = EventStatus.Scheduled;
        Raise(new EventPublished(Id));
        return true;
    }

    /// <summary>Ends the event (manually, or when its last stream goes idle).</summary>
    public bool End(DateTimeOffset at)
    {
        if (Status is not (EventStatus.Scheduled or EventStatus.Live))
        {
            return false;
        }

        Status = EventStatus.Ended;
        EndedAt = at;
        Raise(new EventEnded(Id, at));
        return true;
    }

    /// <summary>
    /// The encoder is sending: the stream is active and a published event goes live (again).
    /// Webhooks older than the stream's last status change are ignored (they can arrive out of order).
    /// </summary>
    public void OnStreamActive(EventStream stream, DateTimeOffset at)
    {
        if (stream.StatusChangedAt is { } changed && at < changed)
        {
            return;
        }

        stream.Status = StreamStatus.Active;
        stream.StatusChangedAt = at;
        Raise(new StreamWentLive(Id, stream.Id, at));

        if (Status is EventStatus.Scheduled or EventStatus.Ended)
        {
            Status = EventStatus.Live;
            LiveStartedAt ??= at;
            EndedAt = null;
        }
    }

    /// <summary>
    /// The encoder stopped for longer than the reconnect window: the stream is idle and the event ends
    /// when no other stream is active. If the stream starts again, the event goes back to live.
    /// </summary>
    public void OnStreamIdle(EventStream stream, DateTimeOffset at)
    {
        if (stream.StatusChangedAt is { } changed && at < changed)
        {
            return;
        }

        stream.Status = StreamStatus.Idle;
        stream.StatusChangedAt = at;
        Raise(new StreamWentIdle(Id, stream.Id, at));

        if (Status == EventStatus.Live && Streams.All(s => s.Status != StreamStatus.Active))
        {
            End(at);
        }
    }

    public void OnRecordingReady(EventStream stream, StreamRecording recording)
    {
        if (!stream.PrimaryRecordingChosenByAdmin)
        {
            stream.SelectLongestRecordingAsPrimary();
        }

        Raise(new RecordingReady(Id, stream.Id, recording.Id));
    }

    /// <summary>The playback id for replay: the stream's primary recording, if ready.</summary>
    public string? ReplayPlaybackId =>
        PrimaryStream?.Recordings.FirstOrDefault(r => r.IsPrimary && r.Status == RecordingStatus.Ready)?.PlaybackId;
}
