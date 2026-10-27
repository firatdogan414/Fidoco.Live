using System.Text.RegularExpressions;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Tenants.Infrastructure;
using Fidoco.Modules.Tenants.Integration;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Tenants.Features;

/// <summary>The organizer edits its own logo, colours and support address.</summary>
internal static partial class AdminBrandingEndpoints
{
    public sealed record BrandingRequest(string? LogoUrl, string? PrimaryColor, string? AccentColor, string? SupportEmail);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut("/admin/branding", Update).RequireAuthorization(AuthPolicies.TenantAdmin);

    private static async Task<IResult> Update(BrandingRequest request, TenantsDbContext db, TenantDirectory directory, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().MaxLength("logoUrl", request.LogoUrl, 500);
        if (request.PrimaryColor is null || !Color().IsMatch(request.PrimaryColor))
        {
            errors.Add("primaryColor", "invalid_color");
        }

        if (request.AccentColor is null || !Color().IsMatch(request.AccentColor))
        {
            errors.Add("accentColor", "invalid_color");
        }

        if (!string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            errors.Email("supportEmail", request.SupportEmail);
        }

        if (!string.IsNullOrWhiteSpace(request.LogoUrl) && !request.LogoUrl.StartsWith('/') && !request.LogoUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            errors.Add("logoUrl", "invalid_url");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        // Tenants are platform data (no tenant filter): select the signed-in admin's own tenant explicitly.
        var entity = await db.Tenants.Include(t => t.Domains).SingleAsync(t => t.Id == tenant.RequiredTenantId, cancellationToken);
        entity.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        entity.PrimaryColor = request.PrimaryColor!;
        entity.AccentColor = request.AccentColor!;
        entity.SupportEmail = string.IsNullOrWhiteSpace(request.SupportEmail) ? null : Emails.Normalize(request.SupportEmail);
        await db.SaveChangesAsync(cancellationToken);
        directory.Invalidate(entity.Id, entity.Domains.Select(d => d.Host));
        return Results.NoContent();
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Color();
}
