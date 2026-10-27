using Fidoco.Contracts.Catalog;

namespace Fidoco.Modules.Playback.Domain;

internal enum PlaybackMode
{
    Live,
    Replay,
}

internal sealed record AccessDecision(PlaybackMode? Mode, string? PlaybackId, string? DenyReason, DateTimeOffset? AccessUntil)
{
    public bool Allowed => Mode is not null;

    public static AccessDecision Deny(string reason) => new(null, null, reason, null);
}

/// <summary>Who may watch what, and when. Pure function so every rule is unit-tested.</summary>
internal static class AccessPolicy
{
    public static AccessDecision Evaluate(Entitlement? entitlement, EventPlaybackInfo ev, DateTimeOffset now)
    {
        if (entitlement is null || entitlement.Status != EntitlementStatus.Active || ev.Status == EventStatus.Draft)
        {
            return AccessDecision.Deny("no_ticket");
        }

        switch (ev.Status)
        {
            case EventStatus.Scheduled:
                return AccessDecision.Deny("not_started");

            case EventStatus.Live when !entitlement.IncludesLive:
                return AccessDecision.Deny("live_not_included");

            case EventStatus.Live when ev.LivePlaybackId is null:
                return AccessDecision.Deny("not_started");

            case EventStatus.Live:
                return new AccessDecision(PlaybackMode.Live, ev.LivePlaybackId, null, null);

            default:
                if (entitlement.ReplayAccessDays is not { } days)
                {
                    return AccessDecision.Deny("no_replay");
                }

                var until = (ev.EndedAt ?? now).AddDays(days);
                if (now >= until)
                {
                    return AccessDecision.Deny("access_expired");
                }

                return ev.ReplayPlaybackId is null
                    ? AccessDecision.Deny("replay_not_ready")
                    : new AccessDecision(PlaybackMode.Replay, ev.ReplayPlaybackId, null, until);
        }
    }
}
