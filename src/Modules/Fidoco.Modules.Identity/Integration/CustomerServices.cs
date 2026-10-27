using System.Security.Claims;
using Fidoco.Contracts.Identity;
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
    public async Task<CustomerAccount> GetOrCreateAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Emails.Normalize(email);
        var existing = await FindIdAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            return new CustomerAccount(existing.Value, Created: false);
        }

        var customer = new Customer { Id = Guid.CreateVersion7(), Email = normalized, CreatedAt = time.GetUtcNow() };
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new CustomerAccount(customer.Id, Created: true);
        }
        catch (DbUpdateException)
        {
            // Created concurrently (unique index on tenant + email): use the winner.
            db.Entry(customer).State = EntityState.Detached;
            var winner = await FindIdAsync(normalized, cancellationToken) ?? throw new InvalidOperationException("Customer could not be created.");
            return new CustomerAccount(winner, Created: false);
        }
    }

    public Task<CustomerInfo?> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerInfo(c.Id, c.Email))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Guid?> FindIdAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Email == normalizedEmail)
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class CustomerLoginLinks(
    IdentityDbContext db,
    ITenantContext tenantContext,
    ITenantDirectory tenants,
    PublicUrls urls,
    TimeProvider time) : ICustomerLoginLinks
{
    public async Task<string> CreateReceiptLinkAsync(Guid customerId, string returnPath, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantContext.RequiredTenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");
        var (token, hash) = SecureTokens.Create();
        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            TokenHash = hash,
            Purpose = LoginTokenPurpose.Receipt,
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

    public Task SignInAsync(HttpContext context, Guid customerId, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(FidocoClaims.UserId, customerId.ToString()),
                new Claim(FidocoClaims.TenantId, tenantContext.RequiredTenantId.ToString()),
            ],
            AuthSchemes.Customer);
        return context.SignInAsync(AuthSchemes.Customer, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }
}
