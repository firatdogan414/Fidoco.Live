using System.Text.Json;
using Fidoco.Modules.Playback.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Playback.Sessions;

internal sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>A session without heartbeats for this long no longer holds the ticket (2.5 heartbeats).</summary>
    public int SessionTimeoutSeconds { get; set; } = 75;

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

internal sealed record PlaybackSession(
    string SessionId,
    Guid TenantId,
    Guid CustomerId,
    Guid EventId,
    Guid EntitlementId,
    PlaybackMode Mode,
    DateTimeOffset? AccessUntil);

/// <summary>
/// Playback sessions in IDistributedCache (memory now, Redis later), never in the database per heartbeat.
/// "active:{entitlementId}" holds the one session allowed to play a ticket; starting a new session
/// overwrites it, which replaces the old one.
/// </summary>
internal sealed class PlaybackSessionStore(IDistributedCache cache, IOptions<PlaybackOptions> options)
{
    private static readonly DistributedCacheEntryOptions SessionRecordLifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12) };
    private static readonly DistributedCacheEntryOptions RevokedLifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) };

    private DistributedCacheEntryOptions ActiveLifetime => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.SessionTimeoutSeconds) };

    public async Task SaveAsync(PlaybackSession session, CancellationToken cancellationToken) =>
        await cache.SetStringAsync(SessionKey(session.SessionId), JsonSerializer.Serialize(session), SessionRecordLifetime, cancellationToken);

    public async Task<PlaybackSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(SessionKey(sessionId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<PlaybackSession>(json);
    }

    public Task<string?> GetActiveSessionIdAsync(Guid entitlementId, CancellationToken cancellationToken) =>
        cache.GetStringAsync(ActiveKey(entitlementId), cancellationToken);

    /// <summary>Makes <paramref name="sessionId"/> the one session for the ticket (and extends its lease).</summary>
    public Task SetActiveAsync(Guid entitlementId, string sessionId, CancellationToken cancellationToken) =>
        cache.SetStringAsync(ActiveKey(entitlementId), sessionId, ActiveLifetime, cancellationToken);

    public Task ClearActiveAsync(Guid entitlementId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(ActiveKey(entitlementId), cancellationToken);

    public Task MarkRevokedAsync(Guid entitlementId, CancellationToken cancellationToken) =>
        cache.SetStringAsync(RevokedKey(entitlementId), "1", RevokedLifetime, cancellationToken);

    public async Task<bool> IsRevokedAsync(Guid entitlementId, CancellationToken cancellationToken) =>
        await cache.GetStringAsync(RevokedKey(entitlementId), cancellationToken) is not null;

    private static string SessionKey(string sessionId) => $"playback:session:{sessionId}";

    private static string ActiveKey(Guid entitlementId) => $"playback:active:{entitlementId}";

    private static string RevokedKey(Guid entitlementId) => $"playback:revoked:{entitlementId}";
}
