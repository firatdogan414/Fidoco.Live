using System.Collections.Concurrent;

namespace Fidoco.Modules.Catalog.Infrastructure.Video;

/// <summary>
/// In-memory video provider for local development without a Mux account and for tests. Only allowed in
/// Development/Testing. Statuses can be changed with <see cref="SetStatus"/>.
/// </summary>
internal sealed class FakeVideoProvider : IVideoProvider
{
    private readonly ConcurrentDictionary<string, (string Status, string StreamKey)> _streams = new();

    public string RtmpUrl => "rtmp://localhost:1935/fake";

    public string RtmpsUrl => "rtmps://localhost:1936/fake";

    public Task<CreatedLiveStream> CreateLiveStreamAsync(LiveStreamSettings settings, CancellationToken cancellationToken)
    {
        var id = $"fake-ls-{Guid.NewGuid():N}";
        _streams[id] = ("idle", NewKey());
        return Task.FromResult(new CreatedLiveStream(id, $"fake-pb-{Guid.NewGuid():N}"));
    }

    public Task<LiveStreamDetails> GetLiveStreamAsync(string providerStreamId, CancellationToken cancellationToken)
    {
        var (status, key) = _streams.GetValueOrDefault(providerStreamId, ("idle", NewKey()));
        return Task.FromResult(new LiveStreamDetails(providerStreamId, status, key));
    }

    public Task<string> ResetStreamKeyAsync(string providerStreamId, CancellationToken cancellationToken)
    {
        var key = NewKey();
        _streams.AddOrUpdate(providerStreamId, ("idle", key), (_, current) => (current.Status, key));
        return Task.FromResult(key);
    }

    public void SetStatus(string providerStreamId, string status) =>
        _streams.AddOrUpdate(providerStreamId, (status, NewKey()), (_, current) => (status, current.StreamKey));

    private static string NewKey() => Guid.NewGuid().ToString("N");
}
