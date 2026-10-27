using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Tenancy;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Features;

/// <summary>Password login for the organizer's admins (TenantAdmin) on the tenant host.</summary>
internal static class AdminAuthEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record AdminMeResponse(string Email, string Role);

    public sealed record ResetRequest(string? Email);

    public sealed record SetPasswordRequest(string? Token, string? Password);

    public sealed record InviteRequest(string? Email);

    public sealed record AdminUserDto(Guid Id, string Email, bool HasPassword, DateTimeOffset? LastLoginAt);

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin");
        admin.MapPost("/auth/login", Login).RequireRateLimiting(RateLimitPolicies.Login);
        admin.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthSchemes.Admin);
            return Results.NoContent();
        });
        admin.MapGet("/me", (HttpContext context) => Results.Ok(new AdminMeResponse(
                context.User.FindFirstValue(FidocoClaims.Email) ?? "",
                context.User.FindFirstValue(ClaimTypes.Role) ?? "")))
            .RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapPost("/auth/reset-request", RequestReset).RequireRateLimiting(RateLimitPolicies.Login);
        admin.MapPost("/auth/set-password", SetPassword).RequireRateLimiting(RateLimitPolicies.Login);
        admin.MapGet("/users", ListUsers).RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapPost("/users", Invite).RequireAuthorization(AuthPolicies.TenantAdmin);
    }

    /// <summary>Always 202 so the response does not reveal whether the address is an admin.</summary>
    private static async Task<IResult> RequestReset(ResetRequest request, HttpContext context, IdentityDbContext db, AdminAccounts accounts, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Accepted();
        }

        var email = Emails.Normalize(request.Email);
        var user = await db.AdminUsers.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (user is not null)
        {
            await accounts.SendPasswordLinkAsync(AdminKind.Tenant, user.Id, tenant.RequiredTenantId, user.Email, AdminTokenPurpose.Reset, context.Request.Host.Host, cancellationToken);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> SetPassword(SetPasswordRequest request, AdminAccounts accounts, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < AdminAccounts.MinimumPasswordLength)
        {
            return new ValidationErrors().Add("password", "password_too_short").ToResult();
        }

        return await accounts.SetPasswordAsync(AdminKind.Tenant, tenant.RequiredTenantId, request.Token ?? "", request.Password, cancellationToken)
            ? Results.NoContent()
            : ApiResults.BadRequest("invalid_or_expired_link");
    }

    private static async Task<IResult> ListUsers(IdentityDbContext db, CancellationToken cancellationToken) =>
        Results.Ok(await db.AdminUsers.AsNoTracking()
            .OrderBy(a => a.Email)
            .Select(a => new AdminUserDto(a.Id, a.Email, a.PasswordHash != null, a.LastLoginAt))
            .ToListAsync(cancellationToken));

    private static async Task<IResult> Invite(InviteRequest request, HttpContext context, AdminInvitations invitations, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Email("email", request.Email);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var invited = await invitations.InviteTenantAdminAsync(tenant.RequiredTenantId, request.Email!, context.Request.Host.Host, cancellationToken);
        return invited ? Results.Accepted() : ApiResults.Conflict("admin_exists");
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext context,
        IdentityDbContext db,
        IPasswordHasher<AdminUser> hasher,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return ApiResults.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials");
        }

        var email = Emails.Normalize(request.Email);
        var user = await db.AdminUsers.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
        var result = user?.PasswordHash is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (user is null || result == PasswordVerificationResult.Failed)
        {
            return ApiResults.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        if (string.IsNullOrEmpty(user.SecurityStamp))
        {
            user.SecurityStamp = SecurityStamps.New(); // admins created before security stamps existed
        }

        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        await AdminAccounts.SignInAsync(context, user.Id, user.Email, user.Role, user.TenantId, user.SecurityStamp);
        return Results.Ok(new AdminMeResponse(user.Email, user.Role));
    }
}
