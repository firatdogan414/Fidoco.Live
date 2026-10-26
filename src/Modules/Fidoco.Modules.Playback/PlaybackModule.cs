using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Playback;

/// <summary>Composition entry point for the Playback module. The only public type in the module.</summary>
public static class PlaybackModule
{
    public static IServiceCollection AddPlaybackModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder api)
    {
        return api;
    }
}
