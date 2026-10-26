using Fidoco.Contracts.Notifications;
using Fidoco.Modules.Notifications.Email;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fidoco.Modules.Notifications;

/// <summary>Composition entry point for the Notifications module. The only public type in the module.</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.AddOptions<EmailOptions>().Bind(section)
            .Validate(o => o.Provider is "Smtp" or "File", "Email:Provider must be 'Smtp' or 'File'.")
            .Validate(o => o.Provider != "Smtp" || !string.IsNullOrEmpty(o.Smtp.Host), "Email:Smtp:Host is required for the Smtp provider.")
            .Validate<IHostEnvironment>(
                (o, env) => o.Provider != "File" || env.IsDevelopment() || env.IsEnvironment("Testing"),
                "Email:Provider 'File' is only allowed in Development; configure Smtp.")
            .ValidateOnStart();

        services.AddSingleton<EmailTemplateRenderer>();
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();
        if (section["Provider"] == "Smtp")
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, FileEmailSender>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder api) => api;
}
