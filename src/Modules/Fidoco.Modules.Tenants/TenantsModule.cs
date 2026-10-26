using Fidoco.Contracts.Tenants;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Tenants.Features;
using Fidoco.Modules.Tenants.Infrastructure;
using Fidoco.Modules.Tenants.Integration;
using Fidoco.SharedKernel.Hosting;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Tenants;

/// <summary>Composition entry point for the Tenants module. The only public type in the module.</summary>
public static class TenantsModule
{
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TenantsDbContext>(configuration);
        services.AddOptions<PlatformOptions>().Bind(configuration.GetSection(PlatformOptions.SectionName));
        services.AddMemoryCache();
        services.AddScoped<TenantDirectory>();
        services.AddScoped<ITenantDirectory>(sp => sp.GetRequiredService<TenantDirectory>());
        services.AddScoped<IDevelopmentSeeder, TenantsDevelopmentSeeder>();
        return services;
    }

    /// <summary>Resolves the tenant from the Host header. Must run before authentication.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();

    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder api)
    {
        GetCurrentTenant.Map(api);
        PlatformTenantEndpoints.Map(api);
        return api;
    }
}
