using System.Security.Claims;
using Fidoco.Contracts.Identity;
using Fidoco.Contracts.Notifications;
using Fidoco.Contracts.Tenants;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fidoco.Modules.Identity.Integration;

internal sealed class CustomerAccounts(IdentityDbContext db, TimeProvider time) : ICustomerAccounts
{
    public async Task<Guid> GetOrCreateAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Emails.Normalize(email);
        var existing = await FindIdAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var customer = new Customer { Id = Guid.CreateVersion7(), Email = normalized, CreatedAt = time.GetUtcNow() };
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return customer.Id;
        }
        catch (DbUpdateException)
        {
            // Created concurrently (unique index on tenant + email): use the winner.
            db.Entry(customer).State = EntityState.Detached;
            return await FindIdAsync(normalized, cancellationToken) ?? throw new InvalidOperationException("Customer could not be created.");
        }
    }

    public Task<CustomerInfo?> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerInfo(c.Id, c.Email))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerInfo>> GetManyAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new CustomerInfo(c.Id, c.Email))
            .ToListAsync(cancellationToken);

    public Task<CustomerProfile?> GetProfileAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerProfile(c.Id, c.Email, c.CreatedAt, c.LastLoginAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerProfile>> SearchAsync(string emailPart, int max, CancellationToken cancellationToken)
    {
        var normalized = Emails.Normalize(emailPart);
        if (normalized.Length == 0)
        {
            return [];
        }

        // E-mails are stored normalized (lower-case), so a plain Contains is case-insensitive on every provider.
        return await db.Customers.AsNoTracking()
            .Where(c => c.Email.Contains(normalized))
            .OrderBy(c => c.Email == normalized ? 0 : 1)
            .ThenBy(c => c.Email)
            .Take(max)
            .Select(c => new CustomerProfile(c.Id, c.Email, c.CreatedAt, c.LastLoginAt))
            .ToListAsync(cancellationToken);
    }

    private Task<Guid?> FindIdAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Email == normalizedEmail)
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class CustomerLoginLinks(
    IdentityDbContext db,
    IEmailDispatcher email,
    ITenantContext tenantContext,
    ITenantDirectory tenants,
    PublicUrls urls,
    TimeProvider time) : ICustomerLoginLinks
{
    public static readonly TimeSpan SupportLinkLifetime = TimeSpan.FromHours(24);

    public Task<string> CreateReceiptLinkAsync(Guid customerId, string returnPath, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        CreateLinkAsync(customerId, LoginTokenPurpose.Receipt, returnPath, expiresAt, cancellationToken);

    public async Task<bool> SendSupportLinkAsync(Guid customerId, string returnPath, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        // Single use like a link from the login page, but valid longer: the buyer may read it hours later.
        var link = await CreateLinkAsync(customerId, LoginTokenPurpose.Login, returnPath, time.GetUtcNow() + SupportLinkLifetime, cancellationToken);
        await email.SendAsync(
            new EmailRequest(customer.Email, EmailTemplates.SupportLoginLink, new Dictionary<string, string> { ["link"] = link }),
            cancellationToken);
        return true;
    }

    private async Task<string> CreateLinkAsync(Guid customerId, LoginTokenPurpose purpose, string returnPath, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantContext.RequiredTenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");
        var (token, hash) = SecureTokens.Create();
        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            TokenHash = hash,
            Purpose = purpose,
            ReturnPath = PublicUrls.SafeReturnPath(returnPath),
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        return urls.For(tenant.PrimaryHost, CustomerSignIn.VerifyPath(token));
    }
}

internal sealed class CustomerSignIn(ITenantContext tenantContext) : ICustomerSignIn
{
    public static string VerifyPath(string token) => $"/auth/verifiera?token={Uri.EscapeDataString(token)}";

    public Task SignInAsync(HttpContext context, Guid customerId, CancellationToken cancellationToken) =>
        SignInAsync(context, customerId, []);

    public Task SignInForPurchaseAsync(HttpContext context, Guid customerId, IReadOnlyList<Guid> purchaseLineIds, CancellationToken cancellationToken) =>
        purchaseLineIds.Count == 0
            ? throw new ArgumentException("A purchase session needs at least one purchase line.", nameof(purchaseLineIds))
            : SignInAsync(context, customerId, purchaseLineIds.Select(id => new Claim(FidocoClaims.PurchaseLine, id.ToString())));

    private Task SignInAsync(HttpContext context, Guid customerId, IEnumerable<Claim> scope)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(FidocoClaims.UserId, customerId.ToString()),
                new Claim(FidocoClaims.TenantId, tenantContext.RequiredTenantId.ToString()),
                .. scope,
            ],
            AuthSchemes.Customer);
        return context.SignInAsync(AuthSchemes.Customer, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }
}
