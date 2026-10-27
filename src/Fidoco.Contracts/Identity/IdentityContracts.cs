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
    /// <summary>Full customer account (signed in with a magic link).</summary>
    public const string Customer = "Customer";

    /// <summary>Watching only: a full account or a session limited to one purchase (see <see cref="FidocoClaims.PurchaseLine"/>).</summary>
    public const string Viewer = "Viewer";

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

    /// <summary>
    /// A purchase line the session may watch. Present only on sessions from direct sign-in after a checkout,
    /// which never get account access: the e-mail typed on the payment page is unverified.
    /// </summary>
    public const string PurchaseLine = "fidoco:pline";

    /// <summary>The signed-in user's id (customer or admin), or null.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(UserId), out var id) ? id : null;

    public static Guid GetRequiredUserId(this ClaimsPrincipal user) =>
        user.GetUserId() ?? throw new InvalidOperationException("No signed-in user.");

    /// <summary>
    /// The customer whose account is signed in, or null for anonymous users, admins and sessions limited to a
    /// purchase (those may watch that purchase but must not act as the account).
    /// </summary>
    public static Guid? GetCustomerId(this ClaimsPrincipal user) =>
        user.GetPurchaseLineScope() is null ? user.GetViewerCustomerId() : null;

    /// <summary>The customer of any customer session, including one limited to a purchase. For watching only.</summary>
    public static Guid? GetViewerCustomerId(this ClaimsPrincipal user) =>
        user.Identity?.AuthenticationType == AuthSchemes.Customer ? user.GetUserId() : null;

    /// <summary>The purchase lines a limited session may watch, or null for a full account session.</summary>
    public static IReadOnlyList<Guid>? GetPurchaseLineScope(this ClaimsPrincipal user)
    {
        var lines = user.FindAll(PurchaseLine).Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty).ToList();
        return lines.Count == 0 ? null : lines;
    }
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

    /// <summary>Signs in a session that may only watch <paramref name="purchaseLineIds"/>, without account access.</summary>
    Task SignInForPurchaseAsync(HttpContext context, Guid customerId, IReadOnlyList<Guid> purchaseLineIds, CancellationToken cancellationToken);
}

/// <summary>
/// A customer's personal data was erased (GDPR). Other modules remove or anonymize what they hold:
/// Payments keeps amounts and dates for bookkeeping but drops the link to the person.
/// </summary>
public sealed record CustomerDeleted(Guid CustomerId) : Fidoco.SharedKernel.Domain.IDomainEvent;
