using Fidoco.Modules.Payments.Features;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.Modules.Payments.Infrastructure.Gateway;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fidoco.Modules.Payments;

/// <summary>Composition entry point for the Payments module. The only public type in the module.</summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration);
        services.AddOptions<StripeOptions>().Bind(configuration.GetSection(StripeOptions.SectionName));
        services.AddOptions<PaymentsOptions>().Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Validate<IHostEnvironment>(
                (o, env) => o.Provider != "Fake" || env.IsDevelopment() || env.IsEnvironment("Testing"),
                "Payments:Provider 'Fake' is only allowed in Development.")
            .ValidateOnStart();

        if (IsFake(configuration))
        {
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        }

        services.AddSingleton<IPaymentWebhookParser, StripeWebhookParser>();
        services.AddScoped<PaymentWebhookProcessor>();
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder api)
    {
        CheckoutEndpoints.Map(api);
        StripeWebhookEndpoint.Map(api);
        PurchaseQueryEndpoints.Map(api);
        if (IsFake(api.ServiceProvider.GetRequiredService<IConfiguration>()))
        {
            FakeCheckoutEndpoints.Map(api);
        }

        return api;
    }

    private static bool IsFake(IConfiguration configuration) =>
        configuration[$"{PaymentsOptions.SectionName}:Provider"] == "Fake";
}
