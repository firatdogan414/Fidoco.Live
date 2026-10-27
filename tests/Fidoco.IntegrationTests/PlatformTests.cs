using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests;

public sealed class PlatformTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private const string Password = "Platform-pass-123";

    private sealed record Domain(Guid Id, string Host, bool IsPrimary);

    private sealed record Tenant(Guid Id, string Name, string Slug, decimal CommissionPercent, List<Domain> Domains);

    private sealed record Settlement(string EventTitle, int TicketsSold, long PlatformFee);

    private sealed record TenantSettlement(Guid TenantId, string TenantName, Settlement Settlement);

    [Fact]
    public async Task Platform_admin_signs_in_only_on_the_platform_host()
    {
        var email = await CreatePlatformAdminAsync();
        var tenant = await factory.CreateTenantAsync();

        var onTenant = await factory.ClientFor(tenant.Host).PostAsJsonAsync("/api/v1/platform/auth/login", new { email, password = Password });
        var platform = await PlatformClientAsync(email);
        var me = await platform.GetAsync("/api/v1/platform/me");

        Assert.Equal(HttpStatusCode.Unauthorized, onTenant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_cannot_use_platform_endpoints()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);

        var response = await admin.GetAsync("/api/v1/platform/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_creates_a_tenant_that_resolves_on_its_new_domain()
    {
        var platform = await PlatformClientAsync(await CreatePlatformAdminAsync());
        var slug = "org" + Guid.NewGuid().ToString("N")[..8];

        var created = await platform.PostAsJsonAsync("/api/v1/platform/tenants", new { name = "Ny arrangör", slug, supportEmail = "info@ny.se" }).EnsureSuccessAsync();
        var tenant = (await created.Content.ReadFromJsonAsync<Tenant>())!;
        var duplicate = await platform.PostAsJsonAsync("/api/v1/platform/tenants", new { name = "Igen", slug });
        var branding = await factory.ClientFor($"{slug}.fidoco.se").GetFromJsonAsync<Dictionary<string, object>>("/api/v1/tenant");

        Assert.Equal($"{slug}.fidoco.se", Assert.Single(tenant.Domains).Host);
        Assert.Equal(15m, tenant.CommissionPercent);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("tenant", branding!["kind"].ToString());
    }

    [Fact]
    public async Task Custom_domain_resolves_immediately_and_last_domain_cannot_be_removed()
    {
        var platform = await PlatformClientAsync(await CreatePlatformAdminAsync());
        var tenant = await factory.CreateTenantAsync();
        await factory.ClientFor(tenant.Host).GetAsync("/api/v1/tenant"); // warm the cache
        var custom = $"stream.{Guid.NewGuid():N}.se"[..30].TrimEnd('.') + ".se";

        var added = (await (await platform.PostAsJsonAsync($"/api/v1/platform/tenants/{tenant.Id}/domains", new { host = custom, isPrimary = true }).EnsureSuccessAsync())
            .Content.ReadFromJsonAsync<Tenant>())!;
        var viaCustom = await factory.ClientFor(custom).GetAsync("/api/v1/tenant");
        var original = added.Domains.Single(d => d.Host == tenant.Host);
        await platform.DeleteAsync($"/api/v1/platform/tenants/{tenant.Id}/domains/{original.Id}").EnsureSuccessAsync();
        var oldHost = await factory.ClientFor(tenant.Host).GetAsync("/api/v1/tenant");
        var remaining = added.Domains.Single(d => d.Host == custom);
        var removeLast = await platform.DeleteAsync($"/api/v1/platform/tenants/{tenant.Id}/domains/{remaining.Id}");

        Assert.Equal(HttpStatusCode.OK, viaCustom.StatusCode);
        Assert.True(added.Domains.Single(d => d.Host == custom).IsPrimary);
        Assert.Equal(HttpStatusCode.NotFound, oldHost.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeLast.StatusCode);
    }

    [Fact]
    public async Task Invited_tenant_admin_chooses_a_password_and_signs_in()
    {
        var platform = await PlatformClientAsync(await CreatePlatformAdminAsync());
        var tenant = await factory.CreateTenantAsync();
        var other = await factory.CreateTenantAsync();
        var email = $"invited-{Guid.NewGuid():N}@example.com";

        await platform.PostAsJsonAsync($"/api/v1/platform/tenants/{tenant.Id}/admins", new { email }).EnsureSuccessAsync();
        var mail = factory.Emails.To(email).Single();
        var token = TestIdentity.ExtractToken(mail.Text);
        var client = factory.ClientFor(tenant.Host);

        var wrongTenant = await factory.ClientFor(other.Host).PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "New-password-1" });
        var tooShort = await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "short" });
        var set = await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "New-password-1" });
        var reuse = await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "Other-password-1" });
        await client.SignInAdminAsync(email, "New-password-1");

        Assert.Contains($"https://{tenant.Host}/admin/valj-losenord?token=", mail.Text);
        Assert.Equal(HttpStatusCode.BadRequest, wrongTenant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/me")).StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_resets_a_forgotten_password_and_invites_a_colleague()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.CreateTenantAdminAsync(tenant, "forgot@example.com", "Old-password-1");
        var client = factory.ClientFor(tenant.Host);

        await client.PostAsJsonAsync("/api/v1/admin/auth/reset-request", new { email = "forgot@example.com" });
        var unknown = await client.PostAsJsonAsync("/api/v1/admin/auth/reset-request", new { email = "nobody@example.com" });
        var token = TestIdentity.ExtractToken(factory.Emails.To("forgot@example.com").Single().Text);
        await client.PostAsJsonAsync("/api/v1/admin/auth/set-password", new { token, password = "New-password-2" }).EnsureSuccessAsync();
        await client.SignInAdminAsync("forgot@example.com", "New-password-2");
        await client.PostAsJsonAsync("/api/v1/admin/users", new { email = "colleague@example.com" }).EnsureSuccessAsync();
        var again = await client.PostAsJsonAsync("/api/v1/admin/users", new { email = "colleague@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Empty(factory.Emails.To("nobody@example.com"));
        Assert.Single(factory.Emails.To("colleague@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task New_terms_apply_to_new_purchases_only_and_platform_sees_all_settlements()
    {
        var platform = await PlatformClientAsync(await CreatePlatformAdminAsync());
        var e = await factory.CreatePublishedEventAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "before@example.com");

        await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{e.Tenant.Id}/terms",
            new { commissionPercent = 10, commissionFixedAmountPerTicket = 500, organizerVatRegistered = false }).EnsureSuccessAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "after@example.com");

        var all = await platform.GetFromJsonAsync<List<TenantSettlement>>("/api/v1/platform/settlements");
        var mine = all!.Single(s => s.TenantId == e.Tenant.Id);

        // Before: 15 % of 159,20 = 23,88. After: 10 % of 159,20 + 5,00 = 20,92.
        Assert.Equal(2, mine.Settlement.TicketsSold);
        Assert.Equal(2388 + 2092, mine.Settlement.PlatformFee);
        Assert.Equal(e.Tenant.Name, mine.TenantName);
    }

    private async Task<string> CreatePlatformAdminAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@fidoco.se";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var admin = new PlatformAdmin { Id = Guid.CreateVersion7(), Email = email, CreatedAt = DateTimeOffset.UtcNow };
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformAdmin>>().HashPassword(admin, Password);
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<HttpClient> PlatformClientAsync(string email)
    {
        var client = factory.ClientFor(FidocoApiFactory.PlatformHost);
        await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email, password = Password }).EnsureSuccessAsync();
        return client;
    }
}
