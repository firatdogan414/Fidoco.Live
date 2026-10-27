using Fidoco.Contracts.Identity;
using Fidoco.Modules.Support.Features;
using Fidoco.Modules.Support.Infrastructure;
using Fidoco.Modules.Support.Integration;
using Fidoco.SharedKernel.Domain;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Support;

/// <summary>
/// Composition entry point for the Support module: the organizer's tools for helping buyers (look up a buyer,
/// resend a login link, complimentary tickets) and the log of those actions. The only public type in the module.
/// </summary>
public static class SupportModule
{
    public static IServiceCollection AddSupportModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SupportDbContext>(configuration);
        services.AddScoped<SupportLog>();
        services.AddScoped<IDataRetentionTask, SupportLogRetention>();
        services.AddDomainEventHandler<CustomerDeleted, AnonymizeSupportLogOnCustomerDeleted>();
        return services;
    }

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder api)
    {
        CustomerSupportEndpoints.Map(api);
        CompEndpoints.Map(api);
        return api;
    }
}
