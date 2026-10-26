using Fidoco.Contracts.Identity;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.Modules.Catalog.Infrastructure.Video;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Catalog.Features.Admin;

/// <summary>
/// Stream management: create the Mux live stream, show RTMP URL and stream key for vMix/OBS, and report
/// the encoder status (polled every 10 s by the admin UI).
/// </summary>
internal static class AdminStreamEndpoints
{
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(5);

    public sealed record RecordingDto(Guid Id, string Status, double? DurationSeconds, bool IsPrimary, DateTimeOffset CreatedAt);

    public sealed record StreamDto(
        Guid Id, Guid EventId, string Name, string Status, string RtmpUrl, string RtmpsUrl, string StreamKey,
        string LatencyMode, int ReconnectWindowSeconds, IReadOnlyList<RecordingDto> Recordings);

    public sealed record StreamStatusDto(string Status, string Source);

    public sealed record StreamKeyDto(string StreamKey);

    public sealed record PrimaryRecordingInput(Guid RecordingId);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/admin/events/{eventId:guid}/streams", Create).RequireAuthorization(AuthPolicies.TenantAdmin);
        var streams = api.MapGroup("/admin/streams").RequireAuthorization(AuthPolicies.TenantAdmin);
        streams.MapGet("/{id:guid}", Get);
        streams.MapGet("/{id:guid}/status", GetStatus);
        streams.MapPost("/{id:guid}/reset-key", ResetKey);
        streams.MapPut("/{id:guid}/primary-recording", SelectPrimaryRecording);
    }

    private static async Task<IResult> Create(
        Guid eventId,
        CatalogDbContext db,
        IVideoProvider video,
        IOptions<MuxOptions> mux,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var ev = await db.Events.Include(e => e.Streams).SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (ev is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        if (ev.Streams.Count > 0)
        {
            return ApiResults.Conflict("stream_exists");
        }

        var now = time.GetUtcNow();
        var stream = new EventStream
        {
            Id = Guid.CreateVersion7(now),
            EventId = ev.Id,
            Name = "Huvudsändning",
            LatencyMode = mux.Value.LatencyMode,
            ReconnectWindowSeconds = mux.Value.ReconnectWindowSeconds,
            CreatedAt = now,
        };

        CreatedLiveStream created;
        try
        {
            created = await video.CreateLiveStreamAsync(
                new LiveStreamSettings(stream.LatencyMode, stream.ReconnectWindowSeconds, Passthrough: stream.Id.ToString()),
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            loggers.CreateLogger(typeof(AdminStreamEndpoints)).LogError(ex, "Creating live stream for event {EventId} failed", eventId);
            return ApiResults.Problem(StatusCodes.Status502BadGateway, "video_provider_unavailable");
        }

        stream.ProviderStreamId = created.ProviderStreamId;
        stream.LivePlaybackId = created.PlaybackId;
        stream.Status = StreamStatus.Idle;
        stream.StatusChangedAt = now;
        ev.Streams.Add(stream);
        await db.SaveChangesAsync(cancellationToken);
        return await Get(stream.Id, db, video, cancellationToken);
    }

    private static async Task<IResult> Get(Guid id, CatalogDbContext db, IVideoProvider video, CancellationToken cancellationToken)
    {
        var stream = await db.Streams.AsNoTracking().Include(s => s.Recordings).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream?.ProviderStreamId is null)
        {
            return ApiResults.NotFound("stream_not_found");
        }

        LiveStreamDetails details;
        try
        {
            details = await video.GetLiveStreamAsync(stream.ProviderStreamId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResults.Problem(StatusCodes.Status502BadGateway, "video_provider_unavailable");
        }

        var recordings = stream.Recordings
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RecordingDto(r.Id, r.Status.ToString().ToLowerInvariant(), r.DurationSeconds, r.IsPrimary, r.CreatedAt))
            .ToList();
        return Results.Ok(new StreamDto(stream.Id, stream.EventId, stream.Name, details.Status, video.RtmpUrl, video.RtmpsUrl,
            details.StreamKey, stream.LatencyMode, stream.ReconnectWindowSeconds, recordings));
    }

    /// <summary>
    /// Asks the provider (cached a few seconds) and reconciles our status if a webhook was missed.
    /// Falls back to the stored status when the provider is unreachable.
    /// </summary>
    private static async Task<IResult> GetStatus(
        Guid id,
        CatalogDbContext db,
        IVideoProvider video,
        IMemoryCache cache,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var stream = await db.Streams.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream?.ProviderStreamId is null)
        {
            return ApiResults.NotFound("stream_not_found");
        }

        string? providerStatus;
        try
        {
            providerStatus = await cache.GetOrCreateAsync($"catalog:stream-status:{stream.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = StatusCacheDuration;
                return (await video.GetLiveStreamAsync(stream.ProviderStreamId, cancellationToken)).Status;
            });
        }
        catch (HttpRequestException)
        {
            return Results.Ok(new StreamStatusDto(stream.Status.ToString().ToLowerInvariant(), "stored"));
        }

        var expected = providerStatus switch
        {
            "active" => StreamStatus.Active,
            "idle" => StreamStatus.Idle,
            _ => (StreamStatus?)null,
        };
        if (expected is not null && expected != stream.Status)
        {
            var ev = await db.Events.Include(e => e.Streams).SingleAsync(e => e.Id == stream.EventId, cancellationToken);
            var tracked = ev.Streams.Single(s => s.Id == stream.Id);
            if (expected == StreamStatus.Active)
            {
                ev.OnStreamActive(tracked, time.GetUtcNow());
            }
            else
            {
                ev.OnStreamIdle(tracked, time.GetUtcNow());
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new StreamStatusDto(providerStatus ?? "unknown", "provider"));
    }

    private static async Task<IResult> ResetKey(Guid id, CatalogDbContext db, IVideoProvider video, CancellationToken cancellationToken)
    {
        var providerStreamId = await db.Streams.Where(s => s.Id == id).Select(s => s.ProviderStreamId).SingleOrDefaultAsync(cancellationToken);
        if (providerStreamId is null)
        {
            return ApiResults.NotFound("stream_not_found");
        }

        try
        {
            return Results.Ok(new StreamKeyDto(await video.ResetStreamKeyAsync(providerStreamId, cancellationToken)));
        }
        catch (HttpRequestException)
        {
            return ApiResults.Problem(StatusCodes.Status502BadGateway, "video_provider_unavailable");
        }
    }

    private static async Task<IResult> SelectPrimaryRecording(Guid id, PrimaryRecordingInput input, CatalogDbContext db, CancellationToken cancellationToken)
    {
        var stream = await db.Streams.Include(s => s.Recordings).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream is null)
        {
            return ApiResults.NotFound("stream_not_found");
        }

        if (!stream.SelectPrimaryRecording(input.RecordingId))
        {
            return ApiResults.BadRequest("recording_not_ready");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
