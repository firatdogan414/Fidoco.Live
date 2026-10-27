# TODO

Deliberately left for later phases, in the suggested order. Phase 1 (the MVP) is complete.

## Product roadmap

1. **Combat sports module** with a live-updated fight card over SignalR (`Fidoco.Verticals.CombatSports`,
   see ARCHITECTURE.md). Hook into `StreamWentLive`/`EventEnded`; push card updates through `/hubs/live`.
2. **Predictions and polls** (tippning/omröstningar): per event, results live over SignalR. Consider
   `Mux:LatencyMode=low` (~5 s) so votes are not spoiled by stream delay.
3. **Live chat**: evaluate a hosted service (e.g. Stream, Ably, Azure Web PubSub with moderation) against
   building on the SignalR hub; moderation, rate limits and GDPR are the deciding factors.
4. **More ticket types and access codes**: replay-only, free with registration, access codes. The model already
   has `IncludesLive`, `ReplayAccessDays` and `AccessMode`; entitlements can come from sources other than purchases.
5. **Clip library**: clips from recordings (Mux asset clipping), public or ticket-gated.
6. **Own discount codes** per tenant in the admin (today: Stripe promotion codes created in the Dashboard).
7. **Stripe Connect** for payouts to organizers (replaces the manual settlement; self-billing as an intermediate step).
8. **Invoice payment for B2B** (e.g. corporate tickets).
9. **Several parallel streams per event** (stages/cameras): the model supports it; add UI, per-stream access
   (`TicketTypeStreams`) and a stream picker in the player.
10. **VAT for more countries**: buyers in other EU countries (OSS, VAT by the buyer's country), Stripe Tax.
11. **Redis and Azure SignalR when scaling out**: `AddStackExchangeRedisCache`, a Redis `IViewerPresence`
    (sorted set per event), `Azure:SignalR:ConnectionString`; distributed rate limiting if needed.
12. **Infrastructure as code (Bicep)**: App Service, Azure SQL (DTU), Application Insights, Key Vault,
    managed certificates, DNS.
13. **Physical tickets with capacity**: reservations in `ITicketAvailability` (see ARCHITECTURE.md),
    QR tickets, a scanning app, combined physical + stream orders.

## Technical follow-ups

- Generate the frontend API types from the OpenAPI document (`openapi-typescript`) instead of hand-written types.
- Azure Blob Storage implementation of `IFileStorage` (local disk under `/home` works on App Service today).
- 2FA for admins (TOTP) and a "sign out everywhere" (security stamp) for customers.
- Stronger anti-sharing: Mux playback restrictions (referrer), DRM for premium events.
- Automate custom-domain onboarding (DNS check, App Service binding, certificate) from the platform admin.
- Sending e-mail from the organizer's own domain (per-tenant DKIM).
- More languages: add `web/src/i18n/locales/<lang>` and `Templates/<lang>.json`; language per customer.
- Admin: list of buyers per event and resending the original receipt (buyer lookup, new login links and comps exist).
- Load test the token/heartbeat endpoints (target: 2 000 concurrent viewers per instance) before the first large event.
- Legal review of the terms of purchase and privacy policy drafts (`web/src/i18n/locales/sv/legal.json`).

## Security and correctness follow-ups (from the phase 1 review)

- **Chargebacks:** handle `charge.dispute.created` (and `charge.dispute.closed`). Today a disputed payment keeps
  its access: revoke on dispute (like a refund), restore if the dispute is won, and show disputes in sales and
  settlement.
- **Timing-based enumeration at admin sign-in:** `AdminAuthEndpoints.Login` and `PlatformAuthEndpoints.Login`
  only hash the password when the e-mail exists, so the response time reveals which addresses are admins.
  Verify against a dummy hash when the user is missing (or otherwise equalize the work).
- **Order of Mux asset events:** `MuxWebhookEndpoint.ApplyAsset` applies `video.asset.*` events in arrival
  order (e.g. a late `errored` can overwrite `ready`, or `ready` arrive before `created`). Compare the event's
  `created_at` with a per-recording status timestamp, as is already done for live stream active/idle.
