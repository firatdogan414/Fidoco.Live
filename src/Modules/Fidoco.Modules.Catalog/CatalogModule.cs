using Fidoco.Contracts.Catalog;
using Fidoco.Modules.Catalog.Features.Admin;
using Fidoco.Modules.Catalog.Features.Public;
using Fidoco.Modules.Catalog.Features.Webhooks;
using Fidoco.Modules.Catalog.Infrastructure;
using Fidoco.Modules.Catalog.Infrastructure.Video;
using Fidoco.Modules.Catalog.Integration;
using Fidoco.SharedKernel.Hosting;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fidoco.Modules.Catalog;

/// <summary>Composition entry point for the Catalog module. The only public type in the module.</summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration);
        services.AddMemoryCache();
        services.AddOptions<MuxOptions>().Bind(configuration.GetSection(MuxOptions.SectionName));
        services.AddOptions<VideoOptions>().Bind(configuration.GetSection(VideoOptions.SectionName))
            .Validate<IHostEnvironment>(
                (o, env) => o.Provider != "Fake" || env.IsDevelopment() || env.IsEnvironment("Testing"),
                "Video:Provider 'Fake' is only allowed in Development.")
            .ValidateOnStart();

        if (configuration[$"{VideoOptions.SectionName}:Provider"] == "Fake")
        {
            services.AddSingleton<FakeVideoProvider>();
            services.AddSingleton<IVideoProvider>(sp => sp.GetRequiredService<FakeVideoProvider>());
        }
        else
        {
            services.AddHttpClient<IVideoProvider, MuxVideoProvider>(client =>
            {
                client.BaseAddress = new Uri(MuxVideoProvider.ApiBaseAddress);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }

        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<ITicketAvailability, TicketAvailability>();
        services.AddScoped<IDevelopmentSeeder, CatalogDevelopmentSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        PublicEventEndpoints.Map(api);
        AdminEventEndpoints.Map(api);
        AdminStreamEndpoints.Map(api);
        MuxWebhookEndpoint.Map(api);
        if (api.ServiceProvider.GetRequiredService<IConfiguration>()[$"{VideoOptions.SectionName}:Provider"] == "Fake")
        {
            DevStreamSimulationEndpoints.Map(api);
        }

        return api;
    }
}
