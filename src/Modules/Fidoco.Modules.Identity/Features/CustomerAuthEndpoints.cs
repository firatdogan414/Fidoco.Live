using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Notifications;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Identity.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fidoco.Modules.Identity.Features;

/// <summary>Passwordless login for customers (magic links) and the "who am I" endpoint.</summary>
internal static class CustomerAuthEndpoints
{
    public static readonly TimeSpan LoginLinkLifetime = TimeSpan.FromMinutes(15);

    public sealed record MagicLinkRequest(string? Email, string? ReturnPath);

    public sealed record VerifyRequest(string? Token);

    public sealed record VerifyResponse(string ReturnPath);

    public sealed record MeResponse(string Email);

    /// <summary>"account" (magic link) or "purchase" (direct sign-in after a checkout: watching only).</summary>
    public sealed record SessionResponse(string Scope);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/auth/magic-link", RequestMagicLink).RequireRateLimiting(RateLimitPolicies.Login);
        api.MapPost("/auth/verify", Verify).RequireRateLimiting(RateLimitPolicies.Login);
        api.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthSchemes.Customer);
            return Results.NoContent();
        });
        api.MapGet("/me", GetMe).RequireAuthorization(AuthPolicies.Customer);
        api.MapGet("/auth/session", (HttpContext context) =>
                Results.Ok(new SessionResponse(context.User.GetPurchaseLineScope() is null ? "account" : "purchase")))
            .RequireAuthorization(AuthPolicies.Viewer);
    }

    /// <summary>Always 202 so the response does not reveal whether an account exists.</summary>
    private static async Task<IResult> RequestMagicLink(
        MagicLinkRequest request,
        HttpContext context,
        IdentityDbContext db,
        IEmailDispatcher email,
        PublicUrls urls,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Email("email", request.Email);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var normalized = Emails.Normalize(request.Email!);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Email == normalized, cancellationToken);
        if (customer is null)
        {
            return Results.Accepted();
        }

        var (token, hash) = SecureTokens.Create();
        var now = time.GetUtcNow();
        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customer.Id,
            TokenHash = hash,
            Purpose = LoginTokenPurpose.Login,
            ReturnPath = PublicUrls.SafeReturnPath(request.ReturnPath),
            CreatedAt = now,
            ExpiresAt = now + LoginLinkLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await email.SendAsync(
                new EmailRequest(customer.Email, EmailTemplates.LoginLink,
                    new Dictionary<string, string> { ["link"] = urls.For(context.Request, CustomerSignIn.VerifyPath(token)) }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            loggers.CreateLogger(typeof(CustomerAuthEndpoints)).LogError(ex, "Sending login link failed");
            return ApiResults.Problem(StatusCodes.Status503ServiceUnavailable, "email_unavailable");
        }

        return Results.Accepted();
    }

    private static async Task<IResult> Verify(
        VerifyRequest request,
        HttpContext context,
        IdentityDbContext db,
        ICustomerSignIn signIn,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
        {
            return ApiResults.BadRequest("invalid_or_expired_link");
        }

        var hash = SecureTokens.Hash(request.Token);
        var token = await db.LoginTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (token is null || !token.IsUsable(now))
        {
            return ApiResults.BadRequest("invalid_or_expired_link");
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == token.CustomerId, cancellationToken);
        if (customer is null)
        {
            return ApiResults.BadRequest("invalid_or_expired_link");
        }

        if (token.Purpose == LoginTokenPurpose.Login)
        {
            // Single use: claim the link atomically, so concurrent requests with it cannot all sign in.
            var claimed = await db.LoginTokens
                .Where(t => t.Id == token.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);
            if (claimed == 0)
            {
                return ApiResults.BadRequest("invalid_or_expired_link");
            }
        }
        else
        {
            token.ConsumedAt ??= now; // receipt links are reusable; this records the first use
        }

        customer.LastLoginAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await signIn.SignInAsync(context, customer.Id, cancellationToken);
        return Results.Ok(new VerifyResponse(token.ReturnPath ?? "/"));
    }

    private static async Task<IResult> GetMe(HttpContext context, IdentityDbContext db, CancellationToken cancellationToken)
    {
        var customerId = context.User.GetRequiredUserId();
        var email = await db.Customers.Where(c => c.Id == customerId).Select(c => c.Email).SingleOrDefaultAsync(cancellationToken);
        return email is null ? Results.Unauthorized() : Results.Ok(new MeResponse(email));
    }
}
