using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Playback.Sessions;

/// <summary>
/// Counts active viewers per event. IDistributedCache cannot count or enumerate keys, so this has its
/// own seam: in memory now (exact with one instance), a Redis sorted set when running several instances.
/// </summary>
internal interface IViewerPresence
{
    void Seen(Guid eventId, string sessionId, DateTimeOffset now);

    void Left(Guid eventId, string sessionId);

    int Count(Guid eventId, DateTimeOffset now);
}

internal sealed class InMemoryViewerPresence(IOptions<PlaybackOptions> options) : IViewerPresence
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, DateTimeOffset>> _events = new();

    public void Seen(Guid eventId, string sessionId, DateTimeOffset now) =>
        _events.GetOrAdd(eventId, _ => new ConcurrentDictionary<string, DateTimeOffset>())[sessionId] = now;

    public void Left(Guid eventId, string sessionId)
    {
        if (_events.TryGetValue(eventId, out var sessions))
        {
            sessions.TryRemove(sessionId, out _);
        }
    }

    public int Count(Guid eventId, DateTimeOffset now)
    {
        if (!_events.TryGetValue(eventId, out var sessions))
        {
            return 0;
        }

        var cutoff = now.AddSeconds(-options.Value.SessionTimeoutSeconds);
        foreach (var (sessionId, lastSeen) in sessions)
        {
            if (lastSeen < cutoff)
            {
                sessions.TryRemove(sessionId, out _);
            }
        }

        return sessions.Count;
    }
}
