using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Features;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Hosting;
using Fidoco.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Identity;

/// <summary>Composition entry point for the Identity module. The only public type in the module.</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration);

        services.AddDataProtection()
            .SetApplicationName("Fidoco.Live")
            .PersistKeysToDbContext<IdentityDbContext>();

        services.AddAuthentication(AuthSchemes.Customer)
            .AddCookie(AuthSchemes.Customer, o => ConfigureCookie(o, "fidoco.customer", TimeSpan.FromDays(30)))
            .AddCookie(AuthSchemes.Admin, o => ConfigureCookie(o, "fidoco.admin", TimeSpan.FromHours(12)));

        services.AddScoped<IAuthorizationHandler, TenantMatchHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Customer, p => p
                .AddAuthenticationSchemes(AuthSchemes.Customer)
                .RequireAuthenticatedUser()
                .RequireClaim(FidocoClaims.UserId)
                .AddRequirements(new TenantMatchRequirement()))
            .AddPolicy(AuthPolicies.TenantAdmin, p => p
                .AddAuthenticationSchemes(AuthSchemes.Admin)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.TenantAdmin)
                .AddRequirements(new TenantMatchRequirement()))
            .AddPolicy(AuthPolicies.PlatformAdmin, p => p
                .AddAuthenticationSchemes(AuthSchemes.Admin)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.PlatformAdmin)
                .AddRequirements(new PlatformHostRequirement()));
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, PlatformHostHandler>();

        services.AddScoped<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
        services.AddScoped<IPasswordHasher<PlatformAdmin>, PasswordHasher<PlatformAdmin>>();
        services.AddScoped<AdminAccounts>();
        services.AddScoped<AdminInvitations>();
        services.AddScoped<CustomerErasure>();
        services.AddScoped<IDataRetentionTask, IdentityRetention>();
        services.AddScoped<IDevelopmentSeeder, PlatformAdminDevelopmentSeeder>();
        services.AddHostedService<PlatformAdminBootstrap>();
        services.AddScoped<ICustomerAccounts, CustomerAccounts>();
        services.AddScoped<ICustomerLoginLinks, CustomerLoginLinks>();
        services.AddScoped<ICustomerSignIn, CustomerSignIn>();
        services.AddScoped<IDevelopmentSeeder, IdentityDevelopmentSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        CustomerAuthEndpoints.Map(api);
        AdminAuthEndpoints.Map(api);
        PlatformAuthEndpoints.Map(api);
        CustomerDataEndpoints.Map(api);
        return api;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, string name, TimeSpan lifetime)
    {
        // One cookie per host, so tenants on different domains never share a session.
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = lifetime;
        options.SlidingExpiration = true;

        // An API: answer with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
