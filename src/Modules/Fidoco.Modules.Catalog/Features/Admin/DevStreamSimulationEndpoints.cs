using Fidoco.Contracts.Identity;
using Fidoco.Modules.Catalog.Domain;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.Modules.Catalog.Infrastructure.Video;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Catalog.Features.Admin;

/// <summary>
/// Development only (Video:Provider=Fake): simulates the encoder starting/stopping so the whole flow
/// (live, end, replay) can be tried locally without Mux. The status endpoint picks the change up.
/// </summary>
internal static class DevStreamSimulationEndpoints
{
    public sealed record SimulateRequest(string? Status);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/dev/streams/{id:guid}/simulate", Simulate).RequireAuthorization(AuthPolicies.TenantAdmin);

    private static async Task<IResult> Simulate(Guid id, SimulateRequest request, CatalogDbContext db, FakeVideoProvider video, TimeProvider time, CancellationToken cancellationToken)
    {
        var stream = await db.Streams.Include(s => s.Recordings).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream?.ProviderStreamId is null || request.Status is not ("active" or "idle"))
        {
            return ApiResults.NotFound("stream_not_found");
        }

        video.SetStatus(stream.ProviderStreamId, request.Status);
        if (request.Status == "idle" && stream.Status == StreamStatus.Active)
        {
            // Pretend Mux finished a recording of the broadcast.
            var ev = await db.Events.Include(e => e.Streams).ThenInclude(s => s.Recordings).SingleAsync(e => e.Id == stream.EventId, cancellationToken);
            var tracked = ev.Streams.Single(s => s.Id == id);
            var recording = new StreamRecording
            {
                Id = Guid.CreateVersion7(),
                StreamId = id,
                ProviderAssetId = $"fake-asset-{Guid.NewGuid():N}",
                PlaybackId = $"fake-replay-{Guid.NewGuid():N}",
                DurationSeconds = 3600,
                Status = RecordingStatus.Ready,
                CreatedAt = time.GetUtcNow(),
            };
            tracked.Recordings.Add(recording);
            ev.OnRecordingReady(tracked, recording);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.NoContent();
    }
}
