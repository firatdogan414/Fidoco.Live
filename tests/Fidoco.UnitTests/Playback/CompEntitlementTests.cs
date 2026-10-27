using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Playback;
using Fidoco.Modules.Playback.Domain;

namespace Fidoco.UnitTests.Playback;

public sealed class CompEntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static CompGrant Grant(int? replayDays = 7) =>
        new(Guid.NewGuid(), "press@example.com", Guid.NewGuid(), "AFN 12", Guid.NewGuid(), "Livestream", IncludesLive: true, replayDays, "Press", "admin@afn.test");

    [Fact]
    public void Comp_is_its_own_source_and_announces_itself_for_the_sign_in_mail()
    {
        var grant = Grant();

        var comp = Entitlement.GrantComp(grant, Now);

        Assert.Equal((EntitlementSources.Comp, comp.Id), (comp.SourceType, comp.SourceId));
        Assert.Equal(("Press", "admin@afn.test", Now), (comp.Note, comp.GrantedBy, comp.CreatedAt));
        var granted = Assert.IsType<ComplimentaryTicketGranted>(Assert.Single(comp.DomainEvents));
        Assert.Equal((comp.Id, grant.CustomerId, "press@example.com", grant.EventId, 7), (granted.EntitlementId, granted.CustomerId, granted.Email, granted.EventId, granted.ReplayAccessDays));
    }

    [Fact]
    public void Comp_gives_the_same_access_as_the_ticket_type_until_revoked()
    {
        var comp = Entitlement.GrantComp(Grant(replayDays: null), Now);
        var live = new EventPlaybackInfo(Guid.NewGuid(), EventStatus.Live, Now.AddHours(-1), null, "live-pb", true, null);
        var ended = live with { Status = EventStatus.Ended, EndedAt = Now, ReplayPlaybackId = "replay-pb" };

        Assert.Equal(PlaybackMode.Live, AccessPolicy.Evaluate(comp, live, Now).Mode);
        Assert.Equal("no_replay", AccessPolicy.Evaluate(comp, ended, Now.AddHours(1)).DenyReason);
        Assert.True(comp.Revoke(Now));
        Assert.False(comp.Revoke(Now));
        Assert.Equal("no_ticket", AccessPolicy.Evaluate(comp, live, Now).DenyReason);
    }
}
