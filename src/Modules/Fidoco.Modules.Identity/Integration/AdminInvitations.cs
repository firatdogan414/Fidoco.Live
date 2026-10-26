using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Integration;

/// <summary>Creates tenant admins (without password) and e-mails them an invitation link.</summary>
internal sealed class AdminInvitations(IdentityDbContext db, AdminAccounts accounts, ITenantContext tenantContext, TimeProvider time)
{
    /// <summary>False when the e-mail is already an admin of the tenant.</summary>
    public async Task<bool> InviteTenantAdminAsync(Guid tenantId, string email, string host, CancellationToken cancellationToken)
    {
        using var _ = tenantContext.Use(tenantId);
        var normalized = Emails.Normalize(email);
        if (await db.AdminUsers.AnyAsync(a => a.Email == normalized, cancellationToken))
        {
            return false;
        }

        var user = new AdminUser { Id = Guid.CreateVersion7(), Email = normalized, Role = Roles.TenantAdmin, CreatedAt = time.GetUtcNow() };
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await accounts.SendPasswordLinkAsync(AdminKind.Tenant, user.Id, tenantId, user.Email, AdminTokenPurpose.Invite, host, cancellationToken);
        return true;
    }
}
