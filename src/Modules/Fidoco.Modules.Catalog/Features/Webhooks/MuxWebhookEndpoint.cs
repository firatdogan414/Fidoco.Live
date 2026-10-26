using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.Modules.Catalog.Infrastructure.Video;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Fidoco.SharedKernel.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Catalog.Features.Webhooks;

/// <summary>
/// Mux webhooks: stream status (live/idle) and recordings (assets) for replay. Signature-verified and
/// idempotent (event id stored in the same transaction as the change).
/// </summary>
internal static class MuxWebhookEndpoint
{
    public const string Provider = "mux";

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/webhooks/mux", Handle).AllowAnonymous();

    private static async Task<IResult> Handle(
        HttpRequest request,
        CatalogDbContext db,
        ITenantContext tenant,
        IOptions<MuxOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(MuxWebhookEndpoint));
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (!MuxSignature.IsValid(body, request.Headers["Mux-Signature"].ToString(), options.Value.WebhookSecret, time.GetUtcNow()))
        {
            return ApiResults.BadRequest("invalid_signature");
        }

        MuxWebhook webhook;
        try
        {
            webhook = MuxWebhook.Parse(body);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Unreadable Mux webhook");
            return ApiResults.BadRequest("invalid_payload");
        }

        if (await db.ProcessedWebhookEvents.AnyAsync(w => w.Provider == Provider && w.EventId == webhook.Id, cancellationToken))
        {
            return Results.Ok();
        }

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent { Provider = Provider, EventId = webhook.Id, ProcessedAt = time.GetUtcNow() });

        if (webhook.LiveStreamId is not null)
        {
            // Webhooks arrive on the platform host: find the owning tenant explicitly, then work inside it.
            var owner = await db.Streams.IgnoreQueryFilters()
                .Where(s => s.ProviderStreamId == webhook.LiveStreamId)
                .Select(s => new { s.TenantId, s.EventId, s.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (owner is not null)
            {
                using var _ = tenant.Use(owner.TenantId);
                var ev = await db.Events
                    .Include(e => e.Streams).ThenInclude(s => s.Recordings)
                    .AsSplitQuery()
                    .SingleAsync(e => e.Id == owner.EventId, cancellationToken);
                Apply(ev, ev.Streams.Single(s => s.Id == owner.Id), webhook, time);
                return await SaveAsync(db, logger, webhook, cancellationToken);
            }

            logger.LogInformation("Mux webhook {Type} for unknown live stream {LiveStreamId} ignored", webhook.Type, webhook.LiveStreamId);
        }

        return await SaveAsync(db, logger, webhook, cancellationToken);
    }

    private static void Apply(Event ev, EventStream stream, MuxWebhook webhook, TimeProvider time)
    {
        switch (webhook.Type)
        {
            case "video.live_stream.active":
                ev.OnStreamActive(stream, webhook.CreatedAt);
                break;
            case "video.live_stream.idle":
                ev.OnStreamIdle(stream, webhook.CreatedAt);
                break;
            case "video.asset.created":
            case "video.asset.ready":
            case "video.asset.live_stream_completed":
            case "video.asset.errored":
                ApplyAsset(ev, stream, webhook, time);
                break;
        }
    }

    private static void ApplyAsset(Event ev, EventStream stream, MuxWebhook webhook, TimeProvider time)
    {
        var recording = stream.Recordings.SingleOrDefault(r => r.ProviderAssetId == webhook.ObjectId);
        if (recording is null)
        {
            recording = new StreamRecording
            {
                Id = Guid.CreateVersion7(),
                StreamId = stream.Id,
                ProviderAssetId = webhook.ObjectId,
                CreatedAt = time.GetUtcNow(),
            };
            stream.Recordings.Add(recording);
        }

        recording.PlaybackId ??= webhook.SignedPlaybackId;
        recording.DurationSeconds = webhook.DurationSeconds ?? recording.DurationSeconds;
        switch (webhook.Type)
        {
            case "video.asset.errored":
                recording.Status = RecordingStatus.Errored;
                break;
            case "video.asset.ready":
            case "video.asset.live_stream_completed":
                if (recording.PlaybackId is not null)
                {
                    recording.Status = RecordingStatus.Ready;
                    ev.OnRecordingReady(stream, recording);
                }

                break;
        }
    }

    private static async Task<IResult> SaveAsync(CatalogDbContext db, ILogger logger, MuxWebhook webhook, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent delivery of the same event won the insert of the dedupe row.
            logger.LogInformation(ex, "Mux webhook {WebhookId} already processed", webhook.Id);
        }

        return Results.Ok();
    }
}

/// <summary>The parts of a Mux webhook we use.</summary>
internal sealed record MuxWebhook(
    string Id,
    string Type,
    DateTimeOffset CreatedAt,
    string ObjectId,
    string? LiveStreamId,
    string? SignedPlaybackId,
    double? DurationSeconds)
{
    public static MuxWebhook Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString()!;
        var data = root.GetProperty("data");
        var objectId = data.GetProperty("id").GetString()!;

        string? liveStreamId = type.StartsWith("video.live_stream.", StringComparison.Ordinal)
            ? objectId
            : data.TryGetProperty("live_stream_id", out var ls) ? ls.GetString() : null;

        string? playbackId = null;
        if (data.TryGetProperty("playback_ids", out var playbackIds) && playbackIds.ValueKind == JsonValueKind.Array)
        {
            playbackId = playbackIds.EnumerateArray()
                .Where(p => p.GetProperty("policy").GetString() == "signed")
                .Select(p => p.GetProperty("id").GetString())
                .FirstOrDefault();
        }

        double? duration = data.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        var createdAt = DateTimeOffset.Parse(root.GetProperty("created_at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        return new MuxWebhook(root.GetProperty("id").GetString()!, type, createdAt, objectId, liveStreamId, playbackId, duration);
    }
}

/// <summary>Verifies the "Mux-Signature: t=&lt;unix&gt;,v1=&lt;hmac&gt;" header (HMAC-SHA256 over "t.body").</summary>
internal static class MuxSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string body, string header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0].Trim() == "t")
            {
                timestamp = pair[1].Trim();
            }
            else if (pair[0].Trim() == "v1")
            {
                signatures.Add(pair[1].Trim());
            }
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            || (now - DateTimeOffset.FromUnixTimeSeconds(unix)).Duration() > Tolerance)
        {
            return false;
        }

        var expected = Compute(timestamp!, body, secret);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(expected)));
    }

    public static string Compute(string timestamp, string body, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
}
