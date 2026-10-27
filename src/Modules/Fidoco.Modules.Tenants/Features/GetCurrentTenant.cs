using Fidoco.Contracts.Tenants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Fidoco.Modules.Tenants.Features;

internal static class GetCurrentTenant
{
    public sealed record TenantBranding(string Name, string Slug, string? LogoUrl, string PrimaryColor, string AccentColor, string? SupportEmail);

    public sealed record Response(string Kind, TenantBranding? Tenant);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/tenant", (HttpContext context) =>
        {
            if (context.IsPlatformHost())
            {
                return Results.Ok(new Response("platform", null));
            }

            var tenant = context.GetTenant();
            return tenant is null
                ? Results.NotFound()
                : Results.Ok(new Response("tenant", new TenantBranding(
                    tenant.Name, tenant.Slug, tenant.LogoUrl, tenant.PrimaryColor, tenant.AccentColor, tenant.SupportEmail)));
        }).AllowAnonymous();
}
