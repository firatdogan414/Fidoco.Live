using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class IdentityTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Me(string Email);

    [Fact]
    public async Task Auth_and_checkout_cookies_are_secure_outside_development_even_behind_a_plain_http_hop()
    {
        // Behind a TLS-terminating proxy without forwarded headers the app sees plain HTTP.
        var e = await factory.CreatePublishedEventAsync();
        var email = $"secure-{Guid.NewGuid():N}@example.com";
        await factory.CreateCustomerAsync(e.Tenant, email);
        await factory.CreateTenantAdminAsync(e.Tenant, email, "Secure-pass-1");
        var http = PlainHttpClient(e.Tenant.Host);

        var token = await factory.RequestLoginTokenAsync(http, email);
        var customer = await http.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var admin = await http.PostAsJsonAsync("/api/v1/admin/auth/login", new { email, password = "Secure-pass-1" });
        var checkout = await http.PostAsJsonAsync("/api/v1/checkout", new { ticketTypeId = e.TicketTypeId });

        AssertSecureCookie(customer, "fidoco.customer");
        AssertSecureCookie(admin, "fidoco.admin");
        AssertSecureCookie(checkout, "fidoco.checkout");
    }

    private HttpClient PlainHttpClient(string host)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{host}"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Fidoco-Client", "web");
        return client;
    }

    private static void AssertSecureCookie(HttpResponseMessage response, string name)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} for {name}");
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_link_used_concurrently_signs_in_only_once()
    {
        var tenant = await factory.CreateTenantAsync();
        var email = $"race-{Guid.NewGuid():N}@example.com";
        await factory.CreateCustomerAsync(tenant, email);
        var token = await factory.RequestLoginTokenAsync(factory.ClientFor(tenant.Host), email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            factory.ClientFor(tenant.Host).PostAsJsonAsync("/api/v1/auth/verify", new { token })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Customer_signs_in_with_magic_link()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(tenant, "kund@example.com");
        var client = factory.ClientFor(tenant.Host);

        var token = await factory.RequestLoginTokenAsync(client, "Kund@Example.com ");
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal("kund@example.com", me!.Email);
        var mail = factory.Emails.To("kund@example.com").Last();
        Assert.Equal($"{tenant.Name} via Fidoco", mail.FromName);
        Assert.Equal($"support@{tenant.Host}", mail.ReplyTo);
        Assert.Contains($"https://{tenant.Host}/auth/verifiera?token=", mail.Text);
    }

    [Fact]
    public async Task Unknown_email_gets_202_and_no_email()
    {
        var tenant = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(tenant.Host).PostAsJsonAsync("/api/v1/auth/magic-link", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.To("nobody@example.com"));
    }

    [Fact]
    public async Task Customer_accounts_are_per_tenant()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(a, "only-a@example.com");

        await factory.ClientFor(b.Host).PostAsJsonAsync("/api/v1/auth/magic-link", new { email = "only-a@example.com" });

        Assert.Empty(factory.Emails.To("only-a@example.com"));
    }

    [Fact]
    public async Task Login_link_can_only_be_used_once()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(tenant, "once@example.com");
        var client = factory.ClientFor(tenant.Host);
        var token = await factory.RequestLoginTokenAsync(client, "once@example.com");

        await client.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var second = await factory.ClientFor(tenant.Host).PostAsJsonAsync("/api/v1/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("invalid_or_expired_link", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_link_expires_after_15_minutes()
    {
        await using var isolated = new FidocoApiFactory();
        var tenant = await isolated.CreateTenantAsync();
        await isolated.CreateCustomerAsync(tenant, "late@example.com");
        var client = isolated.ClientFor(tenant.Host);
        var token = await isolated.RequestLoginTokenAsync(client, "late@example.com");

        isolated.Time.Advance(TimeSpan.FromMinutes(16));
        var response = await client.PostAsJsonAsync("/api/v1/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_link_from_one_tenant_does_not_work_on_another()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(a, "cross@example.com");
        var token = await factory.RequestLoginTokenAsync(factory.ClientFor(a.Host), "cross@example.com");

        var response = await factory.ClientFor(b.Host).PostAsJsonAsync("/api/v1/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_cookie_from_one_tenant_is_rejected_on_another()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(a, "cookie@example.com");
        var clientA = factory.ClientFor(a.Host);
        var token = await factory.RequestLoginTokenAsync(clientA, "cookie@example.com");
        var verify = await clientA.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var cookie = verify.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("fidoco.customer=", StringComparison.Ordinal)).Split(';')[0];

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", cookie);
        var onB = await factory.ClientFor(b.Host).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, onB.StatusCode);
    }

    [Fact]
    public async Task Me_requires_sign_in()
    {
        var tenant = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(tenant.Host).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_signs_in_with_password()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.CreateTenantAdminAsync(tenant, "admin@example.com", "Secret-pass-1");
        var client = factory.ClientFor(tenant.Host);

        var wrong = await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "admin@example.com", password = "wrong" });
        await client.SignInAdminAsync("admin@example.com", "Secret-pass-1");
        var me = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("invalid_credentials", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_cannot_sign_in_on_another_tenant()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await factory.CreateTenantAdminAsync(a, "admin-a@example.com", "Secret-pass-1");

        var response = await factory.ClientFor(b.Host).PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "admin-a@example.com", password = "Secret-pass-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_cookie_does_not_grant_admin_access()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.CreateCustomerAsync(tenant, "not-admin@example.com");
        var client = factory.ClientFor(tenant.Host);
        await factory.SignInCustomerAsync(client, "not-admin@example.com");

        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task State_changing_calls_require_the_client_header()
    {
        var tenant = await factory.CreateTenantAsync();
        var client = factory.ClientFor(tenant.Host);
        client.DefaultRequestHeaders.Remove("X-Fidoco-Client");

        var response = await client.PostAsJsonAsync("/api/v1/auth/magic-link", new { email = "x@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("missing_client_header", await response.Content.ReadAsStringAsync());
    }
}

public sealed class RateLimitTests
{
    private sealed class StrictFactory() : FidocoApiFactory("Sqlite", null, new Dictionary<string, string?> { ["RateLimiting:LoginPerMinute"] = "2" });

    [Fact]
    public async Task Login_endpoints_are_rate_limited()
    {
        await using var factory = new StrictFactory();
        var tenant = await factory.CreateTenantAsync();
        var client = factory.ClientFor(tenant.Host);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/auth/magic-link", new { email = "x@example.com" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }
}
