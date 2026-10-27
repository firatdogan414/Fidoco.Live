using Fidoco.Contracts.Payments;
using Fidoco.Contracts.Playback;
using Fidoco.Modules.Playback.Features;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.Modules.Playback.Integration;
using Fidoco.Modules.Playback.Sessions;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fidoco.Modules.Playback;

/// <summary>Composition entry point for the Playback module. The only public type in the module.</summary>
public static class PlaybackModule
{
    public static IServiceCollection AddPlaybackModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PlaybackDbContext>(configuration);
        services.AddOptions<PlaybackOptions>().Bind(configuration.GetSection(PlaybackOptions.SectionName));
        services.AddOptions<MuxSigningOptions>().Bind(configuration.GetSection(MuxSigningOptions.SectionName))
            .Validate<IHostEnvironment>(
                (o, env) => o.IsConfigured || env.IsDevelopment() || env.IsEnvironment("Testing"),
                "Mux:SigningKeyId and Mux:SigningKeyPrivate are required outside Development.")
            .ValidateOnStart();

        var signing = configuration.GetSection(MuxSigningOptions.SectionName).Get<MuxSigningOptions>() ?? new MuxSigningOptions();
        if (signing.IsConfigured)
        {
            services.AddSingleton<IPlaybackTokenSigner, MuxPlaybackTokenSigner>();
        }
        else
        {
            services.AddSingleton<IPlaybackTokenSigner, FakePlaybackTokenSigner>();
        }

        services.AddSingleton<PlaybackSessionStore>();
        // In memory: exact with one instance. Swap for a Redis implementation when scaling out.
        services.AddSingleton<IViewerPresence, InMemoryViewerPresence>();
        services.AddScoped<ICustomerTickets, CustomerTickets>();
        services.AddScoped<IComplimentaryTickets, ComplimentaryTickets>();
        services.AddDomainEventHandler<PurchaseCompleted, GrantEntitlementsOnPurchase>();
        services.AddDomainEventHandler<PurchaseRefunded, RevokeEntitlementsOnRefund>();
        services.AddDomainEventHandler<Fidoco.Contracts.Identity.CustomerDeleted, DeleteEntitlementsOnCustomerDeleted>();
        return services;
    }

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder api)
    {
        PlaybackEndpoints.Map(api);
        return api;
    }
}
