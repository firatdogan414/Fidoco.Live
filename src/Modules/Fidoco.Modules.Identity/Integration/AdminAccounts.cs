using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Notifications;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Integration;

/// <summary>Invitations, password links and sign-in shared by tenant and platform admins.</summary>
internal sealed class AdminAccounts(
    IdentityDbContext db,
    IEmailDispatcher email,
    IPasswordHasher<AdminUser> tenantHasher,
    IPasswordHasher<PlatformAdmin> platformHasher,
    PublicUrls urls,
    TimeProvider time)
{
    public const int MinimumPasswordLength = 10;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    /// <summary>Creates a link token and e-mails it. <paramref name="host"/> is where the admin UI lives.</summary>
    public async Task SendPasswordLinkAsync(
        AdminKind kind, Guid adminId, Guid? tenantId, string to, AdminTokenPurpose purpose, string host, CancellationToken cancellationToken)
    {
        var (token, hash) = SecureTokens.Create();
        var now = time.GetUtcNow();
        db.AdminTokens.Add(new AdminToken
        {
            Id = Guid.CreateVersion7(now),
            AdminKind = kind,
            AdminId = adminId,
            TenantId = tenantId,
            TokenHash = hash,
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now + (purpose == AdminTokenPurpose.Invite ? InviteLifetime : ResetLifetime),
        });
        await db.SaveChangesAsync(cancellationToken);

        var path = kind == AdminKind.Platform ? "/valj-losenord" : "/admin/valj-losenord";
        await email.SendAsync(
            new EmailRequest(to, purpose == AdminTokenPurpose.Invite ? EmailTemplates.AdminInvite : EmailTemplates.PasswordReset,
                new Dictionary<string, string> { ["link"] = urls.For(host, $"{path}?token={Uri.EscapeDataString(token)}") }),
            cancellationToken);
    }

    /// <summary>
    /// Consumes a link token and sets the password. Returns false for invalid, used or expired links.
    /// A new password ends the admin's existing sessions (new security stamp) and voids every other
    /// outstanding invitation or reset link.
    /// </summary>
    public async Task<bool> SetPasswordAsync(AdminKind kind, Guid? tenantId, string token, string password, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(token);
        var now = time.GetUtcNow();
        var link = await db.AdminTokens.AsNoTracking().SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.AdminKind == kind && t.TenantId == tenantId, cancellationToken);
        if (link is null || link.ConsumedAt is not null || link.ExpiresAt <= now)
        {
            return false;
        }

        // Claim the link atomically, so two concurrent requests cannot both use it.
        var claimed = await db.AdminTokens
            .Where(t => t.Id == link.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        if (kind == AdminKind.Platform)
        {
            var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Id == link.AdminId, cancellationToken);
            if (admin is null) return false;
            admin.PasswordHash = platformHasher.HashPassword(admin, password);
            admin.SecurityStamp = SecurityStamps.New();
        }
        else
        {
            var admin = await db.AdminUsers.SingleOrDefaultAsync(a => a.Id == link.AdminId, cancellationToken);
            if (admin is null) return false;
            admin.PasswordHash = tenantHasher.HashPassword(admin, password);
            admin.SecurityStamp = SecurityStamps.New();
        }

        await db.SaveChangesAsync(cancellationToken);
        await db.AdminTokens
            .Where(t => t.AdminKind == kind && t.AdminId == link.AdminId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);
        return true;
    }

    public static Task SignInAsync(HttpContext context, Guid adminId, string emailAddress, string role, Guid? tenantId, string securityStamp)
    {
        var claims = new List<Claim>
        {
            new(FidocoClaims.UserId, adminId.ToString()),
            new(FidocoClaims.Email, emailAddress),
            new(ClaimTypes.Role, role),
            new(FidocoClaims.SecurityStamp, securityStamp),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(FidocoClaims.TenantId, tenantId.Value.ToString()));
        }

        return context.SignInAsync(AuthSchemes.Admin, new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Admin)));
    }
}

/// <summary>Platform admin endpoints only work on the platform host (e.g. admin.fidoco.se).</summary>
internal sealed class PlatformHostRequirement : Microsoft.AspNetCore.Authorization.IAuthorizationRequirement;

internal sealed class PlatformHostHandler(IHttpContextAccessor accessor) : Microsoft.AspNetCore.Authorization.AuthorizationHandler<PlatformHostRequirement>
{
    protected override Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, PlatformHostRequirement requirement)
    {
        if (accessor.HttpContext is { } http && Fidoco.Contracts.Tenants.TenantHttpContextExtensions.IsPlatformHost(http))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
