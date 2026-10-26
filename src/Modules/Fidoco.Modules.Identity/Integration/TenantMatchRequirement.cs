using Fidoco.Contracts.Identity;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Fidoco.Modules.Identity.Integration;

/// <summary>The signed-in user must belong to the tenant of the current host.</summary>
internal sealed class TenantMatchRequirement : IAuthorizationRequirement;

internal sealed class TenantMatchHandler(ITenantContext tenantContext) : AuthorizationHandler<TenantMatchRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantMatchRequirement requirement)
    {
        var claim = context.User.FindFirst(FidocoClaims.TenantId)?.Value;
        if (tenantContext.TenantId is { } tenantId && Guid.TryParse(claim, out var userTenant) && userTenant == tenantId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
