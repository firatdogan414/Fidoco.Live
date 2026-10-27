namespace Fidoco.SharedKernel.Http;

/// <summary>Rate limiter policy names; limits are configured in the host (RateLimiting section).</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Checkout = "checkout";
    public const string PlaybackToken = "playback-token";
    public const string Heartbeat = "heartbeat";
}
