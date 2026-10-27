using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class SupportTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Customer(Guid Id, string Email);

    private sealed record Purchase(Guid PurchaseId, string OrderNumber, Guid EventId, string EventTitle, long AmountPaid, bool Refunded);

    private sealed record Ticket(Guid EntitlementId, Guid EventId, string EventTitle, string Source, bool Revoked, string? AccessMode, string? DenyReason, bool SessionActive);

    private sealed record LogEntry(string AdminEmail, string Action, Guid? CustomerId, string? CustomerEmail, Guid? EventId, string? EventTitle, string? Details, DateTimeOffset CreatedAt);

    private sealed record Details(Customer Customer, List<Purchase> Purchases, List<Ticket> Tickets, List<LogEntry> Log);

    private sealed record Comp(Guid EntitlementId, Guid CustomerId, string? Email, string? Note, string? GrantedBy, bool Revoked);

    private sealed record Settlement(int TicketsSold, int ComplimentaryTickets, long GrossAmount, long NetAmount, long PlatformFee);

    private sealed record Sales(int TicketsSold, long Revenue);

    [Fact]
    public async Task Admin_finds_a_buyer_and_sees_purchase_access_and_active_session()
    {
        var (e, viewer, email) = await factory.LiveEventWithBuyerAsync();

        var before = await DetailsAsync(e, email);
        await viewer.StartSessionOkAsync(e.Event.Id);
        var during = await DetailsAsync(e, email);

        var purchase = Assert.Single(before.Purchases);
        Assert.Equal((e.Event.Id, 19900L, false), (purchase.EventId, purchase.AmountPaid, purchase.Refunded));
        Assert.Equal(12, purchase.OrderNumber.Length);
        var ticket = Assert.Single(before.Tickets);
        Assert.Equal(("purchase", "live", false, e.Event.Title), (ticket.Source, ticket.AccessMode, ticket.SessionActive, ticket.EventTitle));
        Assert.True(Assert.Single(during.Tickets).SessionActive);
    }

    [Fact]
    public async Task Search_matches_part_of_the_address_and_requires_three_characters()
    {
        var e = await factory.CreatePublishedEventAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "Anna.Svensson@Example.com");
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, "anna@example.com");

        var partial = await e.Admin.GetFromJsonAsync<List<Customer>>("/api/v1/admin/support/customers?email=ANNA");
        var exactFirst = await e.Admin.GetFromJsonAsync<List<Customer>>("/api/v1/admin/support/customers?email=anna@example.com");
        var tooShort = await e.Admin.GetAsync("/api/v1/admin/support/customers?email=an");

        Assert.Equal(["anna.svensson@example.com", "anna@example.com"], partial!.Select(c => c.Email));
        Assert.Equal("anna@example.com", exactFirst![0].Email);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task Support_cannot_see_or_act_on_another_tenants_customers()
    {
        var (a, _, email) = await factory.LiveEventWithBuyerAsync();
        var b = await factory.CreatePublishedEventAsync();
        var customerId = await factory.CreateCustomerAsync(a.Tenant, email);

        var search = await b.Admin.GetFromJsonAsync<List<Customer>>($"/api/v1/admin/support/customers?email={email}");
        var details = await b.Admin.GetAsync($"/api/v1/admin/support/customers/{customerId}");
        var link = await b.Admin.PostAsJsonAsync($"/api/v1/admin/support/customers/{customerId}/login-link", new { });

        Assert.Empty(search!);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, link.StatusCode);
    }

    [Fact]
    public async Task Support_endpoints_require_a_tenant_admin()
    {
        var (e, viewer, email) = await factory.LiveEventWithBuyerAsync();
        await factory.SignInCustomerAsync(viewer, email);

        var asViewer = await viewer.GetAsync($"/api/v1/admin/support/customers?email={email}");
        var grant = await viewer.PostAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}/comps", new { email = "x@example.com", ticketTypeId = e.TicketTypeId });

        Assert.Equal(HttpStatusCode.Unauthorized, asViewer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, grant.StatusCode);
    }

    [Fact]
    public async Task New_login_link_signs_the_buyer_in_on_the_watch_page_once_and_is_logged()
    {
        var (e, _, email) = await factory.LiveEventWithBuyerAsync();
        var customer = (await SearchAsync(e, email)).Single();

        var response = await e.Admin.PostAsJsonAsync($"/api/v1/admin/support/customers/{customer.Id}/login-link", new { eventId = e.Event.Id });
        var mail = factory.Emails.To(email).Last();
        var token = TestIdentity.ExtractToken(mail.Text);
        var phone = factory.ClientFor(e.Tenant.Host);
        var verify = await phone.PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var again = await factory.ClientFor(e.Tenant.Host).PostAsJsonAsync("/api/v1/auth/verify", new { token });
        var log = await e.Admin.GetFromJsonAsync<List<LogEntry>>($"/api/v1/admin/support/log?customerId={customer.Id}");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"Ny inloggningslänk från {e.Tenant.Name}", mail.Subject);
        Assert.Contains($"\"returnPath\":\"/e/{e.Event.Slug}/titta\"", await verify.Content.ReadAsStringAsync());
        Assert.Equal("live", (await phone.StartSessionOkAsync(e.Event.Id)).Mode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var entry = log!.First(l => l.Action == "loginLinkSent");
        Assert.Equal((email, e.Event.Id, e.Event.Title), (entry.CustomerEmail, entry.EventId, entry.EventTitle));
        Assert.StartsWith("admin-", entry.AdminEmail);
    }

    [Fact]
    public async Task Viewing_a_customer_is_logged_once_per_ten_minutes()
    {
        var (e, _, email) = await factory.LiveEventWithBuyerAsync();

        await DetailsAsync(e, email);
        await DetailsAsync(e, email);
        factory.Time.Advance(TimeSpan.FromMinutes(11));
        var details = await DetailsAsync(e, email);

        Assert.Equal(2, details.Log.Count(l => l.Action == "customerViewed"));
    }

    [Fact]
    public async Task Comp_gives_access_without_payment_and_mails_a_sign_in_link()
    {
        var e = await factory.CreatePublishedEventAsync();
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        var email = $"press-{Guid.NewGuid():N}@example.com";

        var comp = await GrantAsync(e, email, "Press");
        await factory.DrainOutboxAsync();
        var mail = Assert.Single(factory.Emails.To(email));
        var viewer = factory.ClientFor(e.Tenant.Host);
        await viewer.PostAsJsonAsync("/api/v1/auth/verify", new { token = TestIdentity.ExtractToken(mail.Text) }).EnsureSuccessAsync();
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var details = await DetailsAsync(e, email);

        Assert.Equal(("Press", false), (comp.Note, comp.Revoked));
        Assert.StartsWith("admin-", comp.GrantedBy);
        Assert.Equal($"Din biljett: {e.Event.Title}", mail.Subject);
        Assert.Equal("live", session.Mode);
        Assert.Empty(details.Purchases);
        Assert.Equal(("comp", "live"), (details.Tickets.Single().Source, details.Tickets.Single().AccessMode));
    }

    [Fact]
    public async Task Comps_are_listed_separately_and_count_as_zero_in_sales_and_settlement()
    {
        var e = await factory.CreatePublishedEventAsync();
        await factory.BuyAsync(factory.ClientFor(e.Tenant.Host), e.TicketTypeId, $"buyer-{Guid.NewGuid():N}@example.com");
        await GrantAsync(e, $"sponsor-{Guid.NewGuid():N}@example.com", "Sponsor");
        var revoked = await GrantAsync(e, $"revoked-{Guid.NewGuid():N}@example.com", null);
        await e.Admin.PostAsync($"/api/v1/admin/comps/{revoked.EntitlementId}/revoke", null).EnsureSuccessAsync();

        var comps = await e.Admin.GetFromJsonAsync<List<Comp>>($"/api/v1/admin/events/{e.Event.Id}/comps");
        var sales = await e.Admin.GetFromJsonAsync<Sales>($"/api/v1/admin/events/{e.Event.Id}/sales");
        var settlement = await e.Admin.GetFromJsonAsync<Settlement>($"/api/v1/admin/events/{e.Event.Id}/settlement");
        var csv = await e.Admin.GetStringAsync($"/api/v1/admin/events/{e.Event.Id}/settlement?format=csv");

        Assert.Equal([false, true], comps!.Select(c => c.Revoked));
        Assert.All(comps!, c => Assert.NotNull(c.Email));
        Assert.Equal(new Sales(1, 19900), sales);
        Assert.Equal(new Settlement(1, 1, 19900, 15920, 2388), settlement);
        Assert.Contains("Friplatser;;Kostnadsfria;1;0.00;0.00;0.00;0.00;0.00", csv);
        Assert.Contains("Summa;;;1;199.00;39.80;159.20;23.88;135.32", csv);
    }

    [Fact]
    public async Task Revoking_a_comp_stops_the_running_session_and_is_logged()
    {
        var e = await factory.CreatePublishedEventAsync();
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow()).EnsureSuccessAsync();
        var email = $"guest-{Guid.NewGuid():N}@example.com";
        var comp = await GrantAsync(e, email, null);
        var viewer = factory.ClientFor(e.Tenant.Host);
        await factory.SignInCustomerAsync(viewer, email);
        var session = await viewer.StartSessionOkAsync(e.Event.Id);

        var revoke = await e.Admin.PostAsync($"/api/v1/admin/comps/{comp.EntitlementId}/revoke", null);
        var heartbeat = await viewer.HeartbeatAsync(session.SessionId);
        var restart = await viewer.StartSessionAsync(e.Event.Id);
        var log = await e.Admin.GetFromJsonAsync<List<LogEntry>>($"/api/v1/admin/support/log?eventId={e.Event.Id}");

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, heartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restart.StatusCode);
        Assert.Equal(["compRevoked", "compGranted"], log!.Select(l => l.Action));
        Assert.All(log!, l => Assert.Equal(email, l.CustomerEmail));
    }

    [Fact]
    public async Task Comp_is_refused_when_the_customer_already_has_access_and_can_be_granted_again_after_revoke()
    {
        var (e, _, email) = await factory.LiveEventWithBuyerAsync();
        var guest = $"again-{Guid.NewGuid():N}@example.com";

        var toBuyer = await e.Admin.PostAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}/comps", new { email, ticketTypeId = e.TicketTypeId });
        var first = await GrantAsync(e, guest, null);
        var twice = await e.Admin.PostAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}/comps", new { email = guest, ticketTypeId = e.TicketTypeId });
        await e.Admin.PostAsync($"/api/v1/admin/comps/{first.EntitlementId}/revoke", null).EnsureSuccessAsync();
        var second = await GrantAsync(e, guest, null);

        Assert.Equal(HttpStatusCode.Conflict, toBuyer.StatusCode);
        Assert.Contains("already_has_access", await toBuyer.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.NotEqual(first.EntitlementId, second.EntitlementId);
    }

    [Fact]
    public async Task Comps_are_refused_for_drafts_and_for_other_tenants()
    {
        var a = await factory.CreatePublishedEventAsync();
        var b = await factory.CreatePublishedEventAsync();
        var draft = await a.Admin.CreateEventAsync();
        var comp = await GrantAsync(a, $"x-{Guid.NewGuid():N}@example.com", null);

        var toDraft = await a.Admin.PostAsJsonAsync($"/api/v1/admin/events/{draft.Id}/comps", new { email = "d@example.com", ticketTypeId = draft.Ticket!.Id });
        var wrongEvent = await a.Admin.PostAsJsonAsync($"/api/v1/admin/events/{a.Event.Id}/comps", new { email = "w@example.com", ticketTypeId = draft.Ticket!.Id });
        var otherTenantGrant = await b.Admin.PostAsJsonAsync($"/api/v1/admin/events/{a.Event.Id}/comps", new { email = "o@example.com", ticketTypeId = a.TicketTypeId });
        var otherTenantList = await b.Admin.GetAsync($"/api/v1/admin/events/{a.Event.Id}/comps");
        var otherTenantRevoke = await b.Admin.PostAsync($"/api/v1/admin/comps/{comp.EntitlementId}/revoke", null);

        Assert.Equal(HttpStatusCode.Conflict, toDraft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTenantGrant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTenantList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTenantRevoke.StatusCode);
        Assert.False((await a.Admin.GetFromJsonAsync<List<Comp>>($"/api/v1/admin/events/{a.Event.Id}/comps"))!.Single().Revoked);
    }

    [Fact]
    public async Task Erasing_a_customer_removes_them_from_the_support_log()
    {
        var (e, _, email) = await factory.LiveEventWithBuyerAsync();
        var customer = (await SearchAsync(e, email)).Single();
        await e.Admin.PostAsJsonAsync($"/api/v1/admin/support/customers/{customer.Id}/login-link", new { }).EnsureSuccessAsync();

        await e.Admin.PostAsJsonAsync("/api/v1/admin/customers/delete", new { email }).EnsureSuccessAsync();
        await factory.DrainOutboxAsync();
        var log = await e.Admin.GetFromJsonAsync<List<LogEntry>>("/api/v1/admin/support/log");

        var entry = log!.Single(l => l.Action == "loginLinkSent" && l.CreatedAt >= factory.Time.GetUtcNow().AddMinutes(-1) && l.CustomerId is null && l.AdminEmail.StartsWith("admin-", StringComparison.Ordinal));
        Assert.Null(entry.CustomerEmail);
        Assert.DoesNotContain(log!, l => l.CustomerEmail == email);
    }

    private async Task<List<Customer>> SearchAsync(TestEvent e, string email) =>
        (await e.Admin.GetFromJsonAsync<List<Customer>>($"/api/v1/admin/support/customers?email={Uri.EscapeDataString(email)}"))!;

    private async Task<Details> DetailsAsync(TestEvent e, string email)
    {
        var customer = (await SearchAsync(e, email)).Single();
        return (await e.Admin.GetFromJsonAsync<Details>($"/api/v1/admin/support/customers/{customer.Id}"))!;
    }

    private static async Task<Comp> GrantAsync(TestEvent e, string email, string? note)
    {
        var response = await e.Admin.PostAsJsonAsync($"/api/v1/admin/events/{e.Event.Id}/comps", new { email, ticketTypeId = e.TicketTypeId, note }).EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<Comp>())!;
    }
}
