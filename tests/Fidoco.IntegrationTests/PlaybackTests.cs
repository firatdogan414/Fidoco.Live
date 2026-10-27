using System.Net;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fidoco.IntegrationTests;

public sealed class PlaybackTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private sealed record Viewers(Guid EventId, int ActiveViewers);

    [Fact]
    public async Task Anonymous_viewer_gets_401()
    {
        var e = await factory.CreatePublishedEventAsync();

        var response = await factory.ClientFor(e.Tenant.Host).StartSessionAsync(e.Event.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_customer_without_ticket_gets_403_no_ticket()
    {
        var (e, _, _) = await factory.LiveEventWithBuyerAsync();
        await factory.CreateCustomerAsync(e.Tenant, "no-ticket@example.com");
        var other = factory.ClientFor(e.Tenant.Host);
        await factory.SignInCustomerAsync(other, "no-ticket@example.com");

        var response = await other.StartSessionAsync(e.Event.Id);
        var access = await other.GetFromJsonAsync<AccessDto>($"/api/v1/events/{e.Event.Id}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no_ticket", await response.Content.ReadAsStringAsync());
        Assert.False(access!.HasTicket);
    }

    [Fact]
    public async Task Buyer_gets_signed_mux_tokens_for_the_live_stream()
    {
        var (e, viewer, _) = await factory.LiveEventWithBuyerAsync();

        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var access = await viewer.GetFromJsonAsync<AccessDto>($"/api/v1/events/{e.Event.Id}/access");

        Assert.Equal("live", session.Mode);
        Assert.Equal(30, session.HeartbeatSeconds);
        Assert.Equal((true, "live"), (access!.HasTicket, access.Mode));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(session.Tokens.Playback, new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(FidocoApiFactory.SigningKey.ExportParameters(false)),
            ValidAudience = "v",
            ValidateIssuer = false,
        });
        Assert.True(validation.IsValid);
        Assert.Equal(session.PlaybackId, ((JsonWebToken)validation.SecurityToken).Subject);
    }

    [Fact]
    public async Task Ticket_before_the_event_starts_gets_not_started()
    {
        var e = await factory.CreatePublishedEventAsync();
        var viewer = factory.ClientFor(e.Tenant.Host);
        await factory.BuyAsync(viewer, e.TicketTypeId, $"early-{Guid.NewGuid():N}@example.com");

        var response = await viewer.StartSessionAsync(e.Event.Id);
        var access = await viewer.GetFromJsonAsync<AccessDto>($"/api/v1/events/{e.Event.Id}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("not_started", await response.Content.ReadAsStringAsync());
        Assert.Equal((true, "not_started"), (access!.HasTicket, access.Reason));
    }

    [Fact]
    public async Task Starting_a_second_session_replaces_the_first()
    {
        var (e, laptop, email) = await factory.LiveEventWithBuyerAsync();
        var phone = factory.ClientFor(e.Tenant.Host);
        await factory.SignInCustomerAsync(phone, email);

        var first = await laptop.StartSessionOkAsync(e.Event.Id);
        Assert.Equal(HttpStatusCode.OK, (await laptop.HeartbeatAsync(first.SessionId)).StatusCode);
        var second = await phone.StartSessionOkAsync(e.Event.Id);

        var oldHeartbeat = await laptop.HeartbeatAsync(first.SessionId);
        var newHeartbeat = await phone.HeartbeatAsync(second.SessionId);
        var oldTokens = await laptop.PostAsync($"/api/v1/playback-sessions/{first.SessionId}/tokens", null);

        Assert.Equal(HttpStatusCode.Conflict, oldHeartbeat.StatusCode);
        Assert.Contains("session_replaced", await oldHeartbeat.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, newHeartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oldTokens.StatusCode);
    }

    [Fact]
    public async Task Session_whose_lease_expired_reclaims_the_ticket_if_nobody_else_took_it()
    {
        var (e, viewer, _) = await factory.LiveEventWithBuyerAsync();
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        var cache = factory.Services.GetRequiredService<IDistributedCache>();
        var activeKeys = await FindActiveKeyAsync(cache, session.SessionId);

        await cache.RemoveAsync(activeKeys);
        var heartbeat = await viewer.HeartbeatAsync(session.SessionId);

        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        Assert.Equal(session.SessionId, await cache.GetStringAsync(activeKeys));
    }

    [Fact]
    public async Task Another_customer_cannot_use_someone_elses_session_id()
    {
        var (e, viewer, _) = await factory.LiveEventWithBuyerAsync();
        var session = await viewer.StartSessionOkAsync(e.Event.Id);
        await factory.CreateCustomerAsync(e.Tenant, "thief@example.com");
        var thief = factory.ClientFor(e.Tenant.Host);
        await factory.SignInCustomerAsync(thief, "thief@example.com");

        var heartbeat = await thief.HeartbeatAsync(session.SessionId);
        var tokens = await thief.PostAsync($"/api/v1/playback-sessions/{session.SessionId}/tokens", null);

        Assert.Equal(HttpStatusCode.NotFound, heartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tokens.StatusCode);
    }

    [Fact]
    public async Task Refund_revokes_access_and_stops_the_running_session()
    {
        var e = await factory.CreatePublishedEventAsync();
        var viewer = factory.ClientFor(e.Tenant.Host);
        var (_, sessionId) = await factory.StartCheckoutAsync(viewer, e.TicketTypeId);
        await factory.PostStripeWebhookAsync("checkout.session.completed", TestPayments.SessionObject(sessionId, "r@example.com", paymentIntent: "pi_r_" + sessionId));
        await factory.DrainOutboxAsync();
        await viewer.GetAsync($"/api/v1/checkout/{factory.Payments.Checkouts.Single(c => c.Value.SessionId == sessionId).Key}");
        await factory.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, factory.Time.GetUtcNow());
        var session = await viewer.StartSessionOkAsync(e.Event.Id);

        await factory.PostStripeWebhookAsync("charge.refunded", TestPayments.ChargeObject("pi_r_" + sessionId));
        await factory.DrainOutboxAsync();

        var heartbeat = await viewer.HeartbeatAsync(session.SessionId);
        var restart = await viewer.StartSessionAsync(e.Event.Id);
        Assert.Equal(HttpStatusCode.Forbidden, heartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restart.StatusCode);
        Assert.Contains("no_ticket", await restart.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Replay_is_available_after_the_event_until_the_window_closes()
    {
        await using var isolated = new FidocoApiFactory();
        var e = await isolated.CreatePublishedEventAsync();
        var viewer = isolated.ClientFor(e.Tenant.Host);
        await isolated.BuyAsync(viewer, e.TicketTypeId, "replay@example.com");
        var t0 = isolated.Time.GetUtcNow();
        await isolated.PostMuxWebhookAsync("video.live_stream.active", e.ProviderStreamId, t0);
        await isolated.PostMuxWebhookAsync("video.live_stream.idle", e.ProviderStreamId, t0.AddHours(3));
        await isolated.PostMuxWebhookAsync("video.asset.ready", "asset-1", t0.AddHours(3), liveStreamId: e.ProviderStreamId, playbackId: "replay-playback", duration: 10800);
        await Task.Delay(TimeSpan.FromSeconds(5.2)); // the catalog's playback info cache is wall-clock based

        var replay = await viewer.StartSessionOkAsync(e.Event.Id);
        isolated.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromHours(4)); // window: 7 days from the end (t0 + 3 h)
        var expiredHeartbeat = await viewer.HeartbeatAsync(replay.SessionId);
        var expiredStart = await viewer.StartSessionAsync(e.Event.Id);

        Assert.Equal(("replay", "replay-playback"), (replay.Mode, replay.PlaybackId));
        Assert.Equal(HttpStatusCode.Forbidden, expiredHeartbeat.StatusCode);
        Assert.Contains("access_expired", await expiredHeartbeat.Content.ReadAsStringAsync());
        Assert.Contains("access_expired", await expiredStart.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ticket_from_one_tenant_gives_no_access_on_another()
    {
        var (a, _, email) = await factory.LiveEventWithBuyerAsync();
        var b = await factory.CreatePublishedEventAsync();
        await factory.CreateCustomerAsync(b.Tenant, email);
        var onB = factory.ClientFor(b.Tenant.Host);
        await factory.SignInCustomerAsync(onB, email);

        var foreignEvent = await onB.StartSessionAsync(a.Event.Id);
        var ownEvent = await onB.StartSessionAsync(b.Event.Id);

        Assert.Equal(HttpStatusCode.NotFound, foreignEvent.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ownEvent.StatusCode);
    }

    [Fact]
    public async Task Admin_sees_active_viewers()
    {
        var (e, first, _) = await factory.LiveEventWithBuyerAsync();
        var second = factory.ClientFor(e.Tenant.Host);
        await factory.BuyAsync(second, e.TicketTypeId, $"second-{Guid.NewGuid():N}@example.com");

        var s1 = await first.StartSessionOkAsync(e.Event.Id);
        await second.StartSessionOkAsync(e.Event.Id);
        var two = await e.Admin.GetFromJsonAsync<Viewers>($"/api/v1/admin/events/{e.Event.Id}/viewers");
        await first.PostAsync($"/api/v1/playback-sessions/{s1.SessionId}/end", null);
        var one = await e.Admin.GetFromJsonAsync<Viewers>($"/api/v1/admin/events/{e.Event.Id}/viewers");
        var otherTenant = await factory.CreatePublishedEventAsync();
        var foreign = await otherTenant.Admin.GetAsync($"/api/v1/admin/events/{e.Event.Id}/viewers");

        Assert.Equal(2, two!.ActiveViewers);
        Assert.Equal(1, one!.ActiveViewers);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private static async Task<string> FindActiveKeyAsync(IDistributedCache cache, string sessionId)
    {
        var json = await cache.GetStringAsync($"playback:session:{sessionId}");
        var entitlementId = System.Text.Json.JsonDocument.Parse(json!).RootElement.GetProperty("EntitlementId").GetGuid();
        return $"playback:active:{entitlementId}";
    }
}
