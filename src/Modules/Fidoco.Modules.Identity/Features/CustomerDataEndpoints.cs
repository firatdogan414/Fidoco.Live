using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Features;

/// <summary>Right to erasure: the customer deletes the account, or the organizer does it on request.</summary>
internal static class CustomerDataEndpoints
{
    public sealed record DeleteMeRequest(string? ConfirmEmail);

    public sealed record DeleteCustomerRequest(string? Email);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapDelete("/me", DeleteMe).RequireAuthorization(AuthPolicies.Customer);
        api.MapPost("/admin/customers/delete", DeleteByEmail).RequireAuthorization(AuthPolicies.TenantAdmin);
    }

    /// <summary>The customer confirms by typing their e-mail address.</summary>
    private static async Task<IResult> DeleteMe([FromBody] DeleteMeRequest request, HttpContext context, IdentityDbContext db, CustomerErasure erasure, CancellationToken cancellationToken)
    {
        var customerId = context.User.GetRequiredUserId();
        var email = await db.Customers.Where(c => c.Id == customerId).Select(c => c.Email).SingleOrDefaultAsync(cancellationToken);
        if (email is null || request.ConfirmEmail is null || Emails.Normalize(request.ConfirmEmail) != email)
        {
            return ApiResults.BadRequest("confirmation_mismatch");
        }

        await erasure.EraseAsync(customerId, cancellationToken);
        await context.SignOutAsync(AuthSchemes.Customer);
        return Results.NoContent();
    }

    /// <summary>Always 202, whether or not the address had an account.</summary>
    private static async Task<IResult> DeleteByEmail(DeleteCustomerRequest request, IdentityDbContext db, CustomerErasure erasure, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Email("email", request.Email);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var email = Emails.Normalize(request.Email!);
        var customerId = await db.Customers.Where(c => c.Email == email).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(cancellationToken);
        if (customerId is not null)
        {
            await erasure.EraseAsync(customerId.Value, cancellationToken);
        }

        return Results.Accepted();
    }
}
