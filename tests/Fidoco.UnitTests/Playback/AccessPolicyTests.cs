using Fidoco.Contracts.Catalog;
using Fidoco.Modules.Playback.Domain;

namespace Fidoco.UnitTests.Playback;

public sealed class AccessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_or_revoked_entitlement_has_no_ticket()
    {
        Assert.Equal("no_ticket", AccessPolicy.Evaluate(null, Live(), Now).DenyReason);
        Assert.Equal("no_ticket", AccessPolicy.Evaluate(Ticket().Revoked(), Live(), Now).DenyReason);
    }

    [Fact]
    public void Scheduled_event_has_not_started()
    {
        Assert.Equal("not_started", AccessPolicy.Evaluate(Ticket(), Info(EventStatus.Scheduled), Now).DenyReason);
    }

    [Fact]
    public void Draft_event_is_never_watchable()
    {
        Assert.Equal("no_ticket", AccessPolicy.Evaluate(Ticket(), Info(EventStatus.Draft, live: "pb"), Now).DenyReason);
    }

    [Fact]
    public void Live_event_plays_the_live_playback_id()
    {
        var decision = AccessPolicy.Evaluate(Ticket(), Live(), Now);

        Assert.Equal(PlaybackMode.Live, decision.Mode);
        Assert.Equal("live-pb", decision.PlaybackId);
    }

    [Fact]
    public void Replay_only_ticket_cannot_watch_live()
    {
        var ticket = Ticket();
        ticket.IncludesLive = false;

        Assert.Equal("live_not_included", AccessPolicy.Evaluate(ticket, Live(), Now).DenyReason);
    }

    [Fact]
    public void Ended_event_plays_replay_within_window()
    {
        var decision = AccessPolicy.Evaluate(Ticket(replayDays: 7), Ended(Now.AddDays(-6), replay: "replay-pb"), Now);

        Assert.Equal(PlaybackMode.Replay, decision.Mode);
        Assert.Equal("replay-pb", decision.PlaybackId);
        Assert.Equal(Now.AddDays(1), decision.AccessUntil);
    }

    [Fact]
    public void Replay_window_expires()
    {
        Assert.Equal("access_expired", AccessPolicy.Evaluate(Ticket(replayDays: 7), Ended(Now.AddDays(-7), replay: "replay-pb"), Now).DenyReason);
    }

    [Fact]
    public void Ticket_without_replay_or_recording_not_ready()
    {
        Assert.Equal("no_replay", AccessPolicy.Evaluate(Ticket(replayDays: null), Ended(Now, replay: "pb"), Now).DenyReason);
        Assert.Equal("replay_not_ready", AccessPolicy.Evaluate(Ticket(), Ended(Now, replay: null), Now).DenyReason);
    }

    private static Entitlement Ticket(int? replayDays = 7) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = Entitlement.PurchaseSource,
        IncludesLive = true,
        ReplayAccessDays = replayDays,
    };

    private static EventPlaybackInfo Live() => Info(EventStatus.Live, live: "live-pb");

    private static EventPlaybackInfo Ended(DateTimeOffset endedAt, string? replay) =>
        new(Guid.NewGuid(), EventStatus.Ended, endedAt.AddHours(-3), endedAt, "live-pb", false, replay);

    private static EventPlaybackInfo Info(EventStatus status, string? live = null) =>
        new(Guid.NewGuid(), status, Now, null, live, live is not null, null);
}

internal static class EntitlementTestExtensions
{
    public static Entitlement Revoked(this Entitlement entitlement)
    {
        entitlement.Status = EntitlementStatus.Revoked;
        return entitlement;
    }
}
