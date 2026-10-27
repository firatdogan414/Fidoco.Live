using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Identity.Domain;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests.Infrastructure;

internal static partial class TestIdentity
{
    public static async Task<Guid> CreateCustomerAsync(this FidocoApiFactory factory, TestTenant tenant, string email)
    {
        using var scope = factory.Services.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenant.Id);
        return (await scope.ServiceProvider.GetRequiredService<ICustomerAccounts>().GetOrCreateAsync(email, CancellationToken.None)).Id;
    }

    public static async Task CreateTenantAdminAsync(this FidocoApiFactory factory, TestTenant tenant, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenant.Id);
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = new AdminUser { Id = Guid.CreateVersion7(), Email = email, Role = Roles.TenantAdmin, CreatedAt = DateTimeOffset.UtcNow };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>().HashPassword(user, password);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync();
    }

    /// <summary>Requests a magic link and returns the token from the captured email.</summary>
    public static async Task<string> RequestLoginTokenAsync(this FidocoApiFactory factory, HttpClient client, string email)
    {
        var before = factory.Emails.To(email.Trim()).Count;
        var response = await client.PostAsJsonAsync("/api/v1/auth/magic-link", new { email });
        response.EnsureSuccessStatusCode();
        var mail = factory.Emails.To(email.Trim()).Skip(before).Single();
        return ExtractToken(mail.Text);
    }

    /// <summary>Full magic link flow: request link, read the email, verify the token.</summary>
    public static async Task SignInCustomerAsync(this FidocoApiFactory factory, HttpClient client, string email)
    {
        var token = await factory.RequestLoginTokenAsync(client, email);
        var response = await client.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        response.EnsureSuccessStatusCode();
    }

    public static async Task SignInAdminAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
    }

    public static string ExtractToken(string text) => TokenPattern().Match(text).Groups[1].Value;

    [GeneratedRegex("token=([A-Za-z0-9_%\\-]+)")]
    private static partial Regex TokenPattern();
}
