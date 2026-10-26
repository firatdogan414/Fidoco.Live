using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Fidoco.Contracts.Identity;

public static class AuthSchemes
{
    /// <summary>Buyers/viewers, signed in with a magic link. Default scheme.</summary>
    public const string Customer = "Customer";

    /// <summary>Organizer staff (TenantAdmin) and platform owner (PlatformAdmin), signed in with a password.</summary>
    public const string Admin = "Admin";
}

public static class AuthPolicies
{
    public const string Customer = "Customer";
    public const string TenantAdmin = "TenantAdmin";
    public const string PlatformAdmin = "PlatformAdmin";
}

public static class Roles
{
    public const string TenantAdmin = "TenantAdmin";
    public const string PlatformAdmin = "PlatformAdmin";
}

public static class FidocoClaims
{
    public const string UserId = "fidoco:uid";
    public const string TenantId = "fidoco:tid";
    public const string Email = "fidoco:email";

    /// <summary>The signed-in user's id (customer or admin), or null.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(UserId), out var id) ? id : null;

    public static Guid GetRequiredUserId(this ClaimsPrincipal user) =>
        user.GetUserId() ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>The signed-in customer (Customer scheme), or null for anonymous and admin users.</summary>
    public static Guid? GetCustomerId(this ClaimsPrincipal user) =>
        user.Identity?.AuthenticationType == AuthSchemes.Customer ? user.GetUserId() : null;
}

public sealed record CustomerInfo(Guid Id, string Email);

/// <summary>Customer accounts of the current tenant (one account per e-mail and tenant).</summary>
public interface ICustomerAccounts
{
    /// <summary>Finds or creates the customer for <paramref name="email"/>; safe to call concurrently.</summary>
    Task<Guid> GetOrCreateAsync(string email, CancellationToken cancellationToken);

    Task<CustomerInfo?> GetAsync(Guid customerId, CancellationToken cancellationToken);
}

public interface ICustomerLoginLinks
{
    /// <summary>
    /// Creates a reusable login link for a receipt, valid until <paramref name="expiresAt"/>.
    /// The raw token only exists in the returned URL; the database stores its hash.
    /// </summary>
    Task<string> CreateReceiptLinkAsync(Guid customerId, string returnPath, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public interface ICustomerSignIn
{
    /// <summary>Signs the customer in on the current request (sets the customer cookie).</summary>
    Task SignInAsync(HttpContext context, Guid customerId, CancellationToken cancellationToken);
}
