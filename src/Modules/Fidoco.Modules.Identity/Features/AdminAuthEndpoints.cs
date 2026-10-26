using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
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

        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var identity = new ClaimsIdentity(
            [
                new Claim(FidocoClaims.UserId, user.Id.ToString()),
                new Claim(FidocoClaims.TenantId, user.TenantId.ToString()),
                new Claim(FidocoClaims.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
            ],
            AuthSchemes.Admin);
        await context.SignInAsync(AuthSchemes.Admin, new ClaimsPrincipal(identity));
        return Results.Ok(new AdminMeResponse(user.Email, user.Role));
    }
}
