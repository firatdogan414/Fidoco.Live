using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.Modules.Identity.Integration;

/// <summary>
/// Rejects admin cookies whose security stamp no longer matches the account (password changed) or whose
/// account is gone. One indexed lookup per admin request; admin traffic is small.
/// </summary>
internal static class AdminSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var user = context.Principal;
        var stamp = user?.FindFirstValue(FidocoClaims.SecurityStamp);
        if (user?.GetUserId() is not { } adminId || string.IsNullOrEmpty(stamp))
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var cancellationToken = context.HttpContext.RequestAborted;
        // Tenant admins are looked up through the tenant filter: a cookie only counts on its own tenant's host.
        var current = user.IsInRole(Roles.PlatformAdmin)
            ? await db.PlatformAdmins.Where(a => a.Id == adminId).Select(a => a.SecurityStamp).SingleOrDefaultAsync(cancellationToken)
            : await db.AdminUsers.Where(a => a.Id == adminId).Select(a => a.SecurityStamp).SingleOrDefaultAsync(cancellationToken);
        if (current != stamp)
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(AuthSchemes.Admin);
    }
}
