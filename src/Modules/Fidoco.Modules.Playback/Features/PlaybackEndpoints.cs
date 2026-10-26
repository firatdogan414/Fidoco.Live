using System.Security.Cryptography;
using Fidoco.Contracts.Catalog;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Playback.Domain;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.Modules.Playback.Sessions;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Observability;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Playback.Features;

/// <summary>
/// Access checks, playback tokens and one active session per ticket. Heartbeats only touch the
/// distributed cache, so they stay cheap with thousands of concurrent viewers.
/// </summary>
internal static class PlaybackEndpoints
{
    public sealed record AccessResponse(bool HasTicket, string? Mode, string? Reason);

    public sealed record TokensDto(string Playback, string Thumbnail, string Storyboard);

    public sealed record SessionResponse(string SessionId, string Mode, string PlaybackId, TokensDto Tokens, DateTimeOffset TokenExpiresAt, int HeartbeatSeconds);

    public sealed record TokenResponse(string PlaybackId, TokensDto Tokens, DateTimeOffset TokenExpiresAt);

    public sealed record HeartbeatResponse(int HeartbeatSeconds);

    public sealed record ViewersResponse(Guid EventId, int ActiveViewers);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/events/{eventId:guid}/access", GetAccess);
        api.MapPost("/events/{eventId:guid}/playback-sessions", StartSession)
            .RequireAuthorization(AuthPolicies.Customer).RequireRateLimiting(RateLimitPolicies.PlaybackToken);

        var sessions = api.MapGroup("/playback-sessions/{sessionId}").RequireAuthorization(AuthPolicies.Customer);
        sessions.MapPost("/heartbeat", Heartbeat).RequireRateLimiting(RateLimitPolicies.Heartbeat);
        sessions.MapPost("/tokens", RefreshTokens).RequireRateLimiting(RateLimitPolicies.PlaybackToken);
        sessions.MapPost("/end", EndSession);

        api.MapGet("/admin/events/{eventId:guid}/viewers", GetViewers).RequireAuthorization(AuthPolicies.TenantAdmin);
    }

    /// <summary>What the event page shows: buy, watch, or why not. Anonymous callers have no ticket.</summary>
    private static async Task<IResult> GetAccess(Guid eventId, HttpContext context, PlaybackDbContext db, ICatalogQueries catalog, TimeProvider time, CancellationToken cancellationToken)
    {
        var info = await catalog.GetPlaybackInfoAsync(eventId, cancellationToken);
        if (info is null || info.Status == EventStatus.Draft)
        {
            return ApiResults.NotFound("event_not_found");
        }

        if (context.User.GetCustomerId() is not { } customerId)
        {
            return Results.Ok(new AccessResponse(false, null, "not_signed_in"));
        }

        var entitlement = await FindEntitlementAsync(db, customerId, eventId, cancellationToken);
        var decision = AccessPolicy.Evaluate(entitlement, info, time.GetUtcNow());
        return Results.Ok(new AccessResponse(entitlement is not null, Mode(decision.Mode), decision.DenyReason));
    }

    private static async Task<IResult> StartSession(
        Guid eventId,
        HttpContext context,
        PlaybackDbContext db,
        ICatalogQueries catalog,
        PlaybackSessionStore sessions,
        IViewerPresence presence,
        IPlaybackTokenSigner signer,
        ITenantContext tenant,
        IOptions<PlaybackOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var info = await catalog.GetPlaybackInfoAsync(eventId, cancellationToken);
        if (info is null)
        {
            return ApiResults.NotFound("event_not_found");
        }

        var customerId = context.User.GetRequiredUserId();
        var now = time.GetUtcNow();
        var entitlement = await FindEntitlementAsync(db, customerId, eventId, cancellationToken);
        var decision = AccessPolicy.Evaluate(entitlement, info, now);
        if (!decision.Allowed)
        {
            return ApiResults.Forbidden(decision.DenyReason!);
        }

        var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        var previous = await sessions.GetActiveSessionIdAsync(entitlement!.Id, cancellationToken);
        if (previous is not null && previous != sessionId)
        {
            presence.Left(eventId, previous);
            FidocoTelemetry.SessionsReplaced.Add(1);
        }

        await sessions.SaveAsync(new PlaybackSession(sessionId, tenant.RequiredTenantId, customerId, eventId, entitlement.Id, decision.Mode!.Value, decision.AccessUntil), cancellationToken);
        await sessions.SetActiveAsync(entitlement.Id, sessionId, cancellationToken);
        presence.Seen(eventId, sessionId, now);
        FidocoTelemetry.PlaybackSessionsStarted.Add(1);

        var tokens = signer.Sign(decision.PlaybackId!, now + options.Value.TokenLifetime);
        return Results.Ok(new SessionResponse(sessionId, Mode(decision.Mode)!, decision.PlaybackId!, ToDto(tokens), tokens.ExpiresAt, options.Value.HeartbeatSeconds));
    }

    private static async Task<IResult> Heartbeat(
        string sessionId,
        HttpContext context,
        PlaybackSessionStore sessions,
        IViewerPresence presence,
        ITenantContext tenant,
        IOptions<PlaybackOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        FidocoTelemetry.Heartbeats.Add(1);
        var session = await LoadOwnSessionAsync(sessionId, context, sessions, tenant, cancellationToken);
        if (session is null)
        {
            return ApiResults.NotFound("session_unknown");
        }

        var now = time.GetUtcNow();
        var check = await CheckSessionAsync(session, sessions, now, cancellationToken);
        if (check is not null)
        {
            presence.Left(session.EventId, sessionId);
            return check;
        }

        await sessions.SetActiveAsync(session.EntitlementId, sessionId, cancellationToken);
        presence.Seen(session.EventId, sessionId, now);
        return Results.Ok(new HeartbeatResponse(options.Value.HeartbeatSeconds));
    }

    /// <summary>New tokens for the active session, e.g. when the player reconnects after a long outage.</summary>
    private static async Task<IResult> RefreshTokens(
        string sessionId,
        HttpContext context,
        PlaybackDbContext db,
        ICatalogQueries catalog,
        PlaybackSessionStore sessions,
        IPlaybackTokenSigner signer,
        ITenantContext tenant,
        IOptions<PlaybackOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var session = await LoadOwnSessionAsync(sessionId, context, sessions, tenant, cancellationToken);
        if (session is null)
        {
            return ApiResults.NotFound("session_unknown");
        }

        var now = time.GetUtcNow();
        var check = await CheckSessionAsync(session, sessions, now, cancellationToken);
        if (check is not null)
        {
            return check;
        }

        var info = await catalog.GetPlaybackInfoAsync(session.EventId, cancellationToken);
        var entitlement = await db.Entitlements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == session.EntitlementId, cancellationToken);
        var decision = info is null ? AccessDecision.Deny("no_ticket") : AccessPolicy.Evaluate(entitlement, info, now);
        if (!decision.Allowed)
        {
            return ApiResults.Forbidden(decision.DenyReason!);
        }

        await sessions.SetActiveAsync(session.EntitlementId, sessionId, cancellationToken);
        var tokens = signer.Sign(decision.PlaybackId!, now + options.Value.TokenLifetime);
        return Results.Ok(new TokenResponse(decision.PlaybackId!, ToDto(tokens), tokens.ExpiresAt));
    }

    private static async Task<IResult> EndSession(
        string sessionId,
        HttpContext context,
        PlaybackSessionStore sessions,
        IViewerPresence presence,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var session = await LoadOwnSessionAsync(sessionId, context, sessions, tenant, cancellationToken);
        if (session is not null)
        {
            if (await sessions.GetActiveSessionIdAsync(session.EntitlementId, cancellationToken) == sessionId)
            {
                await sessions.ClearActiveAsync(session.EntitlementId, cancellationToken);
            }

            presence.Left(session.EventId, sessionId);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetViewers(Guid eventId, ICatalogQueries catalog, IViewerPresence presence, TimeProvider time, CancellationToken cancellationToken)
    {
        // The catalog lookup is tenant-filtered, so another tenant's event id gives 404.
        return await catalog.GetEventSummaryAsync(eventId, cancellationToken) is null
            ? ApiResults.NotFound("event_not_found")
            : Results.Ok(new ViewersResponse(eventId, presence.Count(eventId, time.GetUtcNow())));
    }

    /// <summary>Null when the session is fine; otherwise the response that stops the player.</summary>
    private static async Task<IResult?> CheckSessionAsync(PlaybackSession session, PlaybackSessionStore sessions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await sessions.IsRevokedAsync(session.EntitlementId, cancellationToken))
        {
            return ApiResults.Forbidden("no_ticket");
        }

        if (session.AccessUntil is { } until && now >= until)
        {
            return ApiResults.Forbidden("access_expired");
        }

        var active = await sessions.GetActiveSessionIdAsync(session.EntitlementId, cancellationToken);
        if (active is not null && active != session.SessionId)
        {
            return ApiResults.Conflict("session_replaced");
        }

        // active == null: the lease expired (e.g. a sleeping phone) and nobody else took the ticket; reclaim it.
        return null;
    }

    private static async Task<PlaybackSession?> LoadOwnSessionAsync(
        string sessionId, HttpContext context, PlaybackSessionStore sessions, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        return session is not null && session.CustomerId == context.User.GetRequiredUserId() && session.TenantId == tenant.TenantId
            ? session
            : null;
    }

    private static Task<Entitlement?> FindEntitlementAsync(PlaybackDbContext db, Guid customerId, Guid eventId, CancellationToken cancellationToken) =>
        db.Entitlements.AsNoTracking()
            .Where(e => e.CustomerId == customerId && e.EventId == eventId && e.Status == EntitlementStatus.Active)
            .OrderByDescending(e => e.IncludesLive)
            .ThenByDescending(e => e.ReplayAccessDays)
            .FirstOrDefaultAsync(cancellationToken);

    private static string? Mode(PlaybackMode? mode) => mode?.ToString().ToLowerInvariant();

    private static TokensDto ToDto(PlaybackTokens tokens) => new(tokens.Playback, tokens.Thumbnail, tokens.Storyboard);
}
