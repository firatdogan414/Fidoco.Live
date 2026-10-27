using System.Net.Http.Headers;
using System.Text;
using Fidoco.SharedKernel.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Catalog.Infrastructure.Video;

/// <summary>Mux API reachable with our token (lists one live stream, cached a minute).</summary>
internal sealed class MuxHealthCheck(IMemoryCache cache, IHttpClientFactory httpClients, IOptions<MuxOptions> mux, IOptions<VideoOptions> video) : CachedExternalHealthCheck(cache)
{
    protected override string Name => "mux";

    protected override async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        if (video.Value.Provider == "Fake")
        {
            return HealthCheckResult.Healthy("Fake video provider (development)");
        }

        var settings = mux.Value;
        if (string.IsNullOrEmpty(settings.TokenId) || string.IsNullOrEmpty(settings.WebhookSecret))
        {
            return HealthCheckResult.Degraded("Mux:TokenId or Mux:WebhookSecret is not configured");
        }

        using var client = httpClients.CreateClient(nameof(MuxHealthCheck));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{MuxVideoProvider.ApiBaseAddress}video/v1/live-streams?limit=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.TokenId}:{settings.TokenSecret}")));
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"Mux answered {(int)response.StatusCode}");
    }
}
