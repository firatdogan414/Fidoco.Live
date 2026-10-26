using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Tenants;

/// <summary>Composition entry point for the Tenants module. The only public type in the module.</summary>
public static class TenantsModule
{
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder api)
    {
        return api;
    }
}
