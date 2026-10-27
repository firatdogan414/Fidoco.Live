using Fidoco.Contracts.Catalog;
using Fidoco.Modules.Catalog.Domain;

namespace Fidoco.UnitTests.Catalog;

public sealed class EventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Publish_requires_a_ticket_type()
    {
        var ev = NewEvent(withTicket: false);

        Assert.False(ev.Publish());
        Assert.Equal(EventStatus.Draft, ev.Status);
    }

    [Fact]
    public void Stream_active_makes_a_scheduled_event_live()
    {
        var ev = Published(out var stream);

        ev.OnStreamActive(stream, T0);

        Assert.Equal(EventStatus.Live, ev.Status);
        Assert.Equal(T0, ev.LiveStartedAt);
        Assert.Contains(ev.DomainEvents, e => e is StreamWentLive);
    }

    [Fact]
    public void Stream_active_does_not_publish_a_draft()
    {
        var ev = NewEvent();
        var stream = AddStream(ev);

        ev.OnStreamActive(stream, T0);

        Assert.Equal(EventStatus.Draft, ev.Status);
    }

    [Fact]
    public void Stream_idle_ends_a_live_event_and_active_again_resumes_it()
    {
        var ev = Published(out var stream);
        ev.OnStreamActive(stream, T0);

        ev.OnStreamIdle(stream, T0.AddHours(3));
        Assert.Equal(EventStatus.Ended, ev.Status);
        Assert.Equal(T0.AddHours(3), ev.EndedAt);
        Assert.Contains(ev.DomainEvents, e => e is EventEnded);

        ev.OnStreamActive(stream, T0.AddHours(3).AddMinutes(10));
        Assert.Equal(EventStatus.Live, ev.Status);
        Assert.Null(ev.EndedAt);
        Assert.Equal(T0, ev.LiveStartedAt);
    }

    [Fact]
    public void Older_status_changes_are_ignored()
    {
        var ev = Published(out var stream);
        ev.OnStreamActive(stream, T0);
        ev.OnStreamIdle(stream, T0.AddHours(3));

        ev.OnStreamActive(stream, T0.AddHours(1));

        Assert.Equal(EventStatus.Ended, ev.Status);
        Assert.Equal(StreamStatus.Idle, stream.Status);
    }

    [Fact]
    public void Longest_ready_recording_becomes_primary_unless_admin_chose_one()
    {
        var ev = Published(out var stream);
        var short1 = AddRecording(stream, 600);
        ev.OnRecordingReady(stream, short1);
        var long1 = AddRecording(stream, 7200);
        ev.OnRecordingReady(stream, long1);

        Assert.True(long1.IsPrimary);
        Assert.False(short1.IsPrimary);
        Assert.Equal(long1.PlaybackId, ev.ReplayPlaybackId);

        Assert.True(stream.SelectPrimaryRecording(short1.Id));
        var longer = AddRecording(stream, 9000);
        ev.OnRecordingReady(stream, longer);
        Assert.True(short1.IsPrimary);
        Assert.False(longer.IsPrimary);
    }

    [Fact]
    public void Manual_end_only_from_scheduled_or_live()
    {
        var ev = NewEvent();

        Assert.False(ev.End(T0));
        ev.Publish();
        Assert.True(ev.End(T0));
        Assert.False(ev.End(T0));
    }

    private static Event Published(out EventStream stream)
    {
        var ev = NewEvent();
        stream = AddStream(ev);
        ev.Publish();
        ev.ClearDomainEvents();
        return ev;
    }

    internal static Event NewEvent(bool withTicket = true)
    {
        var ev = new Event { Id = Guid.NewGuid(), Slug = "afn-12", Title = "AFN 12", StartsAt = T0 };
        if (withTicket)
        {
            ev.TicketTypes.Add(new TicketType { Id = Guid.NewGuid(), EventId = ev.Id, Name = "Livestream", PriceAmount = 19900, ReplayAccessDays = 7 });
        }

        return ev;
    }

    private static EventStream AddStream(Event ev)
    {
        var stream = new EventStream { Id = Guid.NewGuid(), EventId = ev.Id, Name = "Main", LatencyMode = "standard", Status = StreamStatus.Idle };
        ev.Streams.Add(stream);
        return stream;
    }

    private static StreamRecording AddRecording(EventStream stream, double duration)
    {
        var recording = new StreamRecording
        {
            Id = Guid.NewGuid(),
            StreamId = stream.Id,
            ProviderAssetId = Guid.NewGuid().ToString(),
            PlaybackId = Guid.NewGuid().ToString(),
            DurationSeconds = duration,
            Status = RecordingStatus.Ready,
        };
        stream.Recordings.Add(recording);
        return recording;
    }
}

public sealed class TicketSaleRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_event_is_not_on_sale()
    {
        var ev = EventTests.NewEvent();

        Assert.Equal("event_not_published", TicketSaleRules.NotOnSaleReason(ev, ev.TicketTypes[0], Now));
    }

    [Fact]
    public void Scheduled_and_live_events_are_on_sale()
    {
        var ev = EventTests.NewEvent();
        ev.Publish();

        Assert.Null(TicketSaleRules.NotOnSaleReason(ev, ev.TicketTypes[0], Now));
    }

    [Fact]
    public void Ended_event_is_on_sale_while_replay_window_is_open()
    {
        var ev = EventTests.NewEvent();
        ev.Publish();
        ev.End(Now);

        Assert.Null(TicketSaleRules.NotOnSaleReason(ev, ev.TicketTypes[0], Now.AddDays(6)));
        Assert.Equal("event_ended", TicketSaleRules.NotOnSaleReason(ev, ev.TicketTypes[0], Now.AddDays(7)));
    }

    [Fact]
    public void Ended_event_without_replay_is_not_on_sale()
    {
        var ev = EventTests.NewEvent();
        ev.TicketTypes[0].ReplayAccessDays = null;
        ev.Publish();
        ev.End(Now);

        Assert.Equal("event_ended", TicketSaleRules.NotOnSaleReason(ev, ev.TicketTypes[0], Now));
    }

    [Fact]
    public void Sales_window_and_inactive_tickets_are_respected()
    {
        var ev = EventTests.NewEvent();
        ev.Publish();
        var ticket = ev.TicketTypes[0];

        ticket.SalesStartsAt = Now.AddHours(1);
        Assert.Equal("sales_not_started", TicketSaleRules.NotOnSaleReason(ev, ticket, Now));
        ticket.SalesStartsAt = null;
        ticket.SalesEndsAt = Now;
        Assert.Equal("sales_ended", TicketSaleRules.NotOnSaleReason(ev, ticket, Now));
        ticket.SalesEndsAt = null;
        ticket.IsActive = false;
        Assert.Equal("ticket_inactive", TicketSaleRules.NotOnSaleReason(ev, ticket, Now));
    }
}
