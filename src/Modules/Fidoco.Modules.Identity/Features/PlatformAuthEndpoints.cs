using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Features;

/// <summary>Sign-in for the platform owner (PlatformAdmin), only on the platform host.</summary>
internal static class PlatformAuthEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record MeResponse(string Email, string Role);

    public static void Map(IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/platform");
        platform.MapPost("/auth/login", Login).RequireRateLimiting(RateLimitPolicies.Login);
        platform.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthSchemes.Admin);
            return Results.NoContent();
        });
        platform.MapGet("/me", (HttpContext context) => Results.Ok(new MeResponse(
                context.User.FindFirstValue(FidocoClaims.Email) ?? "", Roles.PlatformAdmin)))
            .RequireAuthorization(AuthPolicies.PlatformAdmin);
        platform.MapPost("/auth/reset-request", RequestReset).RequireRateLimiting(RateLimitPolicies.Login);
        platform.MapPost("/auth/set-password", SetPassword).RequireRateLimiting(RateLimitPolicies.Login);
        platform.MapPost("/tenants/{tenantId:guid}/admins", InviteTenantAdmin).RequireAuthorization(AuthPolicies.PlatformAdmin);
    }

    private static async Task<IResult> Login(LoginRequest request, HttpContext context, IdentityDbContext db, IPasswordHasher<PlatformAdmin> hasher, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!context.IsPlatformHost() || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return ApiResults.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials");
        }

        var email = Emails.Normalize(request.Email);
        var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
        var result = admin?.PasswordHash is null ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password);
        if (admin is null || result == PasswordVerificationResult.Failed)
        {
            return ApiResults.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials");
        }

        admin.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await AdminAccounts.SignInAsync(context, admin.Id, admin.Email, Roles.PlatformAdmin, tenantId: null);
        return Results.Ok(new MeResponse(admin.Email, Roles.PlatformAdmin));
    }

    private static async Task<IResult> RequestReset(AdminAuthEndpoints.ResetRequest request, HttpContext context, IdentityDbContext db, AdminAccounts accounts, CancellationToken cancellationToken)
    {
        if (context.IsPlatformHost() && !string.IsNullOrWhiteSpace(request.Email))
        {
            var email = Emails.Normalize(request.Email);
            var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
            if (admin is not null)
            {
                await accounts.SendPasswordLinkAsync(AdminKind.Platform, admin.Id, null, admin.Email, AdminTokenPurpose.Reset, context.Request.Host.Host, cancellationToken);
            }
        }

        return Results.Accepted();
    }

    private static async Task<IResult> SetPassword(AdminAuthEndpoints.SetPasswordRequest request, HttpContext context, AdminAccounts accounts, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < AdminAccounts.MinimumPasswordLength)
        {
            return new ValidationErrors().Add("password", "password_too_short").ToResult();
        }

        return context.IsPlatformHost() && await accounts.SetPasswordAsync(AdminKind.Platform, null, request.Token ?? "", request.Password, cancellationToken)
            ? Results.NoContent()
            : ApiResults.BadRequest("invalid_or_expired_link");
    }

    private static async Task<IResult> InviteTenantAdmin(Guid tenantId, AdminAuthEndpoints.InviteRequest request, ITenantDirectory tenants, AdminInvitations invitations, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Email("email", request.Email);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var tenant = await tenants.GetAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return ApiResults.NotFound("tenant_not_found");
        }

        return await invitations.InviteTenantAdminAsync(tenantId, request.Email!, tenant.PrimaryHost, cancellationToken)
            ? Results.Accepted()
            : ApiResults.Conflict("admin_exists");
    }
}
