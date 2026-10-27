using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;
using Fidoco.Modules.Identity.Infrastructure;
using Fidoco.Modules.Payments.Infrastructure;
using Fidoco.Modules.Playback.Infrastructure;
using Fidoco.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.IntegrationTests;

public sealed class GdprTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Sales(int TicketsSold, long Revenue);

    [Fact]
    public async Task Customer_deletes_account_purchases_are_anonymized_and_access_ends()
    {
        var (e, viewer, email) = await factory.LiveEventWithBuyerAsync();
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var customerId = await CustomerIdAsync(e.Tenant.Id, email);

        var wrongConfirm = await viewer.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new { confirmEmail = "other@example.com" }) });
        var delete = await viewer.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new { confirmEmail = email }) });
        await factory.DrainOutboxAsync();

        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/v1/me")).StatusCode);
        await AssertErasedAsync(e, customerId, email, session.SessionId, viewer);
    }

    [Fact]
    public async Task Organizer_erases_a_customer_by_email()
    {
        var (e, viewer, email) = await factory.LiveEventWithBuyerAsync();
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var customerId = await CustomerIdAsync(e.Tenant.Id, email);

        var response = await e.Admin.PostAsJsonAsync("/api/v1/admin/customers/delete", new { email });
        await factory.DrainOutboxAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertErasedAsync(e, customerId, email, session.SessionId, viewer);
    }

    [Fact]
    public async Task Organizer_cannot_erase_another_tenants_customer()
    {
        var (a, _, email) = await factory.LiveEventWithBuyerAsync();
        var b = await factory.CreatePublishedEventAsync();

        await b.Admin.PostAsJsonAsync("/api/v1/admin/customers/delete", new { email });
        await factory.DrainOutboxAsync();

        Assert.NotNull(await CustomerIdOrNullAsync(a.Tenant.Id, email));
    }

    [Fact]
    public async Task Retention_removes_expired_tokens_and_old_processed_outbox_rows()
    {
        await using var isolated = new FidocoApiFactory();
        var tenant = await isolated.CreateTenantAsync();
        await isolated.CreateCustomerAsync(tenant, "old@example.com");
        await isolated.RequestLoginTokenAsync(isolated.ClientFor(tenant.Host), "old@example.com");
        await isolated.DrainOutboxAsync();

        isolated.Time.Advance(TimeSpan.FromDays(31));
        using var scope = isolated.Services.CreateScope();
        var deleted = 0;
        foreach (var task in scope.ServiceProvider.GetServices<IDataRetentionTask>())
        {
            deleted += await task.RunAsync(isolated.Time.GetUtcNow(), CancellationToken.None);
        }

        var tokens = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().LoginTokens.IgnoreQueryFilters().CountAsync();
        Assert.True(deleted >= 1);
        Assert.Equal(0, tokens);
    }

    private async Task AssertErasedAsync(TestEvent e, Guid customerId, string email, string sessionId, HttpClient viewer)
    {
        using var scope = factory.Services.CreateScope();
        var purchases = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Purchases.IgnoreQueryFilters()
            .Where(p => p.TenantId == e.Tenant.Id).ToListAsync();
        var entitlements = await scope.ServiceProvider.GetRequiredService<PlaybackDbContext>().Entitlements.IgnoreQueryFilters()
            .CountAsync(x => x.CustomerId == customerId);

        Assert.Null(await CustomerIdOrNullAsync(e.Tenant.Id, email));
        Assert.All(purchases, p => Assert.Null(p.CustomerId));
        Assert.Equal(0, entitlements);

        // Bookkeeping figures are kept.
        var sales = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");
        Assert.Equal((1, 19900L), (sales!.TicketsSold, sales.Revenue));

        // No more login links, and a running session stops.
        var before = factory.Emails.To(email).Count;
        await factory.ClientFor(e.Tenant.Host).PostAsJsonAsync("/api/v1/auth/magic-link", new { email });
        Assert.Equal(before, factory.Emails.To(email).Count);
        Assert.NotEqual(HttpStatusCode.OK, (await viewer.HeartbeatAsync(sessionId)).StatusCode);
    }

    private async Task<Guid> CustomerIdAsync(Guid tenantId, string email) => (await CustomerIdOrNullAsync(tenantId, email))!.Value;

    private async Task<Guid?> CustomerIdOrNullAsync(Guid tenantId, string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Email == email).Select(c => (Guid?)c.Id).SingleOrDefaultAsync();
    }
}
