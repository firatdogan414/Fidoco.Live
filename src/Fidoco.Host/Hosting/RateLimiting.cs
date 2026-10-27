using System.Security.Claims;
using System.Threading.RateLimiting;
using Fidoco.Contracts.Identity;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Fidoco.Host.Hosting;

internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int LoginPerMinute { get; set; } = 10;

    public int CheckoutPerMinute { get; set; } = 10;

    public int PlaybackTokenPerMinute { get; set; } = 10;

    /// <summary>Per customer. Heartbeats come every 30 s from the one active session; the margin allows retries with backoff.</summary>
    public int HeartbeatPerMinute { get; set; } = 6;
}

/// <summary>
/// Per-instance fixed-window limits (in memory). Partitioned by IP for anonymous endpoints and by customer for
/// playback (many viewers can share one IP, e.g. at a pub). Never by a value the caller chooses freely, such
/// as the session id in the URL: every made-up value would get its own budget (and its own partition).
/// </summary>
internal static class RateLimiting
{
    public static IServiceCollection AddFidocoRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ApiResults.Problem(StatusCodes.Status429TooManyRequests, "rate_limited").ExecuteAsync(context.HttpContext);
            };

            limiter.AddPolicy(RateLimitPolicies.Login, ctx => PerMinute(ClientIp(ctx), options.LoginPerMinute));
            limiter.AddPolicy(RateLimitPolicies.Checkout, ctx => PerMinute(ClientIp(ctx), options.CheckoutPerMinute));
            limiter.AddPolicy(RateLimitPolicies.PlaybackToken, ctx =>
                PerMinute(ctx.User.FindFirstValue(FidocoClaims.UserId) ?? ClientIp(ctx), options.PlaybackTokenPerMinute));
            limiter.AddPolicy(RateLimitPolicies.Heartbeat, ctx =>
                PerMinute(ctx.User.FindFirstValue(FidocoClaims.UserId) ?? ClientIp(ctx), options.HeartbeatPerMinute));
        });
        return services;
    }

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    // Behind Azure App Service set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so this is the client's IP.
    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
