# Architecture

Fidoco Live is a **modular monolith**: one ASP.NET Core application, one deployment, six modules with
hard boundaries. The guiding rule is *simple and maintainable over clever*: no microservices, no
mediator library, no generic repositories over EF Core. Endpoints use EF Core directly or a small class
in the same module. Things that will need to scale are **interfaces with one implementation today**, not
infrastructure built in advance.

```
Browser (SPA) ──► Fidoco.Host ── tenant resolution (Host header), auth cookies, CSRF header, rate limits,
                 │               /api/v1/*, /health, /hubs/live (empty), SPA + Open Graph tags
                 │
                 ├── Fidoco.Modules.Tenants        organizers, domains, branding, commercial terms
                 ├── Fidoco.Modules.Identity       customers (magic links), tenant/platform admins
                 ├── Fidoco.Modules.Catalog        events, streams (Mux), recordings, ticket types
                 ├── Fidoco.Modules.Payments       checkout (Stripe), purchases, refunds, settlement
                 ├── Fidoco.Modules.Playback       entitlements, signed tokens, sessions, viewers
                 └── Fidoco.Modules.Notifications  e-mail templates and sending
                        ▲ every module references only Fidoco.Contracts + Fidoco.SharedKernel
```

## Projects and dependency rules

| Project | Contains | May reference |
|---|---|---|
| `Fidoco.SharedKernel` | Tenancy, `ModuleDbContext`, domain events + outbox, error helpers, file storage, health/retention bases | nothing from Fidoco |
| `Fidoco.Contracts` | The API between modules: interfaces (`ICatalogQueries`, `ICustomerAccounts`, …), DTOs and domain events (`PurchaseCompleted`, …). No logic. | SharedKernel |
| `Fidoco.Modules.*` | One module each; only `<Name>Module` (registration and endpoint mapping) is public | SharedKernel, Contracts |
| `Fidoco.Verticals.*` | Industry modules (none yet) | SharedKernel, Contracts |
| `Fidoco.Host` | Composition: Program.cs, middleware, SPA hosting, uploads, health, observability | everything |

Modules never reference each other, so dependency cycles are impossible and the compiler blocks access
to another module's internals. `tests/Fidoco.ArchitectureTests` additionally verifies: no module references
another module or vertical; module types do not depend on other modules' namespaces; only the module
entry point is public (EF migrations excepted); Contracts depends only on SharedKernel; SharedKernel on
nothing; verticals only on Contracts and SharedKernel.

Inside a module, code is organized by feature (`Features/` holds endpoint + logic per use case),
`Domain/` holds entities and rules, `Integration/` implements contracts and event handlers, and
`Infrastructure/` holds the `DbContext`, migrations and external adapters.

## Modules

| Module | Responsibility | Publishes | Handles |
|---|---|---|---|
| **Tenants** | Tenants (organizers), domains, branding, commercial terms; resolves the tenant from the Host header (`ITenantDirectory`, cached briefly) | – | – |
| **Identity** | Customers (one per e-mail and tenant) with magic links; TenantAdmin and PlatformAdmin with passwords, invitations and resets; cookie schemes and policies; Data Protection keys in the database; GDPR erasure | `CustomerDeleted` | – |
| **Catalog** | Events (draft → scheduled → live → ended), streams (Mux live streams, RTMP/key, status), recordings for replay, ticket types (kind, capacity, replay days), Mux webhooks, the capacity seam `ITicketAvailability` | `EventPublished`, `StreamWentLive`, `StreamWentIdle`, `EventEnded`, `RecordingReady` | – |
| **Payments** | Checkout (Stripe), purchases with lines, Stripe webhooks, refunds, sales figures, commission and settlement | `PurchaseCompleted`, `PurchaseRefunded` | `CustomerDeleted` (anonymize) |
| **Playback** | Entitlements (the right to watch), access policy, Mux signed tokens, one session per ticket, heartbeats, active viewers | – | `PurchaseCompleted` (grant), `PurchaseRefunded` (revoke), `CustomerDeleted` (delete) |
| **Notifications** | Branded, localized e-mail templates; sends from Fidoco on behalf of the tenant (`"<Tenant> via Fidoco"`, reply-to the tenant) | – | `PurchaseCompleted` (receipt with magic link) |

The core is industry-neutral: *Event, Stream, TicketType, Purchase, Entitlement*. Nothing about fights,
rounds or results lives here.

## Cross-cutting mechanisms

### Tenancy

- Every tenant-owned entity implements `ITenantOwned`. `ModuleDbContext` adds a named query filter
  (`"Tenant"`) on `TenantId == current tenant`; with no tenant it matches nothing (fail closed).
- On save, `TenantId` is stamped on new rows, and writing a row of another tenant (or moving one) throws.
- The tenant comes from the Host header (`TenantResolutionMiddleware`). Webhooks and outbox handlers set
  it explicitly with `ITenantContext.Use(tenantId)`.
- `IgnoreQueryFilters()` is used only where crossing tenants is the point: webhook lookups by provider id
  (Catalog, Payments), the platform settlement report and retention jobs.
- Cookies are per host; policies also require the user's tenant claim to match the host's tenant.

### Domain events and the outbox

- Entities raise domain events (`AggregateRoot.Raise`). On `SaveChanges`, `ModuleDbContext` writes them to
  the module's outbox table **in the same transaction**, one row per (event × subscribed handler).
- `OutboxProcessor<T>` (one per module) claims rows atomically with a lease, so several instances never
  process the same row, runs the handler in a fresh scope with the event's tenant, and retries failures
  with exponential backoff (2 s, 4 s … 10 min) up to 10 attempts; then the row is marked failed (visible in
  `/health` and logs). A post-commit signal wakes the processor, so side effects normally run within
  milliseconds; polling every 2 s is the fallback.
- Handlers must be idempotent (delivery is at-least-once).
- Subscribe with `services.AddDomainEventHandler<TEvent, THandler>()` in the subscribing module.

| Event | Raised by | Handlers today | Hook points for later |
|---|---|---|---|
| `PurchaseCompleted` | Payments | Playback grants entitlement; Notifications sends receipt | analytics, CRM, physical ticket issuing |
| `PurchaseRefunded` | Payments | Playback revokes access | notification to buyer |
| `CustomerDeleted` | Identity | Payments anonymizes; Playback deletes entitlements | any module holding personal data |
| `EventPublished` | Catalog | – | announcements |
| `StreamWentLive`, `StreamWentIdle`, `EventEnded` | Catalog | – | SignalR push to viewers, "we are live" e-mails, vertical modules |
| `RecordingReady` | Catalog | – | "replay available" e-mails, clip library |

### Purchase flow

```
POST /checkout ──► Catalog: ticket on sale? ──► ITicketAvailability.TryReserve (capacity seam)
                 ──► CheckoutSession (snapshot of price, VAT, access terms, commission terms)
                 ──► Stripe Checkout (dynamic payment methods: card, Swish …) ──► buyer pays
Stripe webhook ──► signature ──► dedupe (event id) ──► find checkout (cross-tenant) ──► Use(tenant)
                 ──► customer get-or-create ──► Purchase + lines + PurchaseCompleted (one transaction)
outbox ──► Playback: Entitlement   ──► Notifications: receipt with reusable magic link
thank-you page ──► GET /checkout/{id}: same browser (HttpOnly nonce) is signed in directly
```

Idempotency is layered: processed webhook ids, "checkout already completed", and a unique purchase per checkout.

### Playback

- `AccessPolicy` (pure function) decides: `no_ticket`, `not_started`, `live`, `live_not_included`,
  `replay` within the window, `replay_not_ready`, `no_replay`, `access_expired`.
- Tokens are Mux signed-playback JWTs (RS256, `kid`, `sub` = playback id, `aud` v/t/s, 15 min), signed locally.
- One session per ticket: `playback:active:{entitlementId}` in `IDistributedCache` holds the current session;
  starting a new session replaces it, the old one gets `409 session_replaced` on its next heartbeat (every 30 s).
  Heartbeats never touch the database. Refunds and erasure mark the entitlement revoked in the cache.
- The client (`PlaybackSessionController`) retries transient failures with backoff without interrupting playback.
- Active viewers per event: `IViewerPresence` (in memory today).

### Authentication

Cookie schemes `Customer` (magic links; tokens stored as SHA-256 hashes; the link opens a page that POSTs the
token, so e-mail scanners cannot consume it) and `Admin` (passwords via `PasswordHasher`). Policies:
`Customer`, `TenantAdmin` (tenant claim must match host) and `PlatformAdmin` (platform host only).
CSRF: SameSite=Lax cookies plus the required `X-Fidoco-Client` header on state-changing API calls.

### Statelessness and scale

No in-memory session state is needed for correctness except what sits behind `IDistributedCache`
(playback sessions) and `IViewerPresence`; both are swapped for Redis before scaling out. Read caches
(tenant lookup, event status, playback info) are short-lived per instance and safe with several instances.
Data Protection keys are in the database, so every instance reads every cookie. Rate limits are per instance.

### Errors, i18n, observability

- API errors are ProblemDetails with a machine-readable `code`; the SPA translates `errors.<code>` (Swedish
  texts in `web/src/i18n/locales/sv`). A test checks that every API error code has a translation.
- E-mail texts live in `src/Modules/Fidoco.Modules.Notifications/Templates/sv.json`.
- OpenTelemetry traces, metrics (`fidoco.*` counters) and logs; Application Insights or OTLP by configuration;
  JSON console logs in production. `/health` reports databases, outbox, Stripe and Mux.

### Business model and settlement

Fidoco is the seller (merchant of record) and reports VAT on the full price. Each tenant has commercial terms
(commission % of revenue excluding VAT, optional fixed amount per ticket, whether the organizer is
VAT-registered). Terms are snapshotted at checkout and each purchase line stores its net amount and commission,
so later changes never alter history. The settlement report (per event, CSV) is the organizer's invoice basis.

## How to add a module

1. Create `src/Modules/Fidoco.Modules.<Name>` (copy an existing `.csproj`: references SharedKernel and
   Contracts only, `InternalsVisibleTo` for the test projects).
2. Add `<Name>Module.cs` with `Add<Name>Module(IServiceCollection, IConfiguration)` and
   `Map<Name>Endpoints(IEndpointRouteBuilder)`; keep every other type `internal`.
3. If it stores data: a `<Name>DbContext : ModuleDbContext` with `ModuleName = "<Name>"` (schema
   `<name>`, outbox table `<Name>Outbox`), globally unique table names, `ITenantOwned` on tenant data,
   a design-time factory, `services.AddModuleDbContext<<Name>DbContext>(configuration)`, and an initial migration.
4. Put what other modules may call into `Fidoco.Contracts/<Name>/` (interfaces, DTOs, events).
5. Register it in `Fidoco.Host` (`Program.cs`) and add the project reference.
6. Architecture tests pick it up automatically (update the expected module list in `ModuleBoundaryTests`).

## How an industry module plugs in (example: combat sports)

`src/Verticals/Fidoco.Verticals.CombatSports` (not built yet) would:

- own schema `combat` with `FightCard(EventId)`, `Bout`, `Fighter`, `Result`; link to the core only through `EventId`;
- read events through `ICatalogQueries`, never Catalog's tables;
- react to `StreamWentLive` / `EventEnded` (and later push live card updates through the SignalR hub);
- expose endpoints such as `GET /api/v1/events/{eventId}/fight-card` and admin endpoints to edit bouts and results;
- provide UI in `web/src/verticals/combat-sports/`, mounted in the slot marked on the event and watch pages.

Concerts, lectures and conferences get their own vertical the same way; the core never learns about them.

## Where physical tickets and capacity go

The checkout already calls the seam in the right places:

1. `ITicketAvailability.TryReserveAsync(ticketTypeId, quantity, expiresAt)` before creating the Stripe session
   (`Payments/Features/CheckoutEndpoints.cs`); the reservation id is stored on the `CheckoutSession` and the
   Stripe session expires with it (30 min).
2. `ConfirmAsync` when the payment webhook completes the purchase, `ReleaseAsync` when the checkout expires or
   fails (`Payments/Features/PaymentWebhookProcessor.cs`).

Today `Catalog/Integration/CatalogQueries.cs` → `TicketAvailability` always succeeds for tickets without
capacity. For physical tickets, implement it there: a `TicketReservations` table in the catalog schema,
a capacity check (sold + active reservations < capacity) in one transaction with row locking, and a
retention task that expires stale reservations. Physical tickets then become purchase lines with
`Kind = PhysicalAdmission`; a ticketing module can listen to `PurchaseCompleted` to issue QR tickets.

## Known limitations

- **One viewing per ticket is enforced by the client for an already loaded stream.** The replaced player stops
  at its next heartbeat, but a modified client that ignores 409 could keep playing a stream it already loaded.
  Short token lifetimes limit link sharing. Stronger protection (DRM, playback restrictions) is in TODO.
- **Several instances require Redis** for the distributed cache and viewer presence (the code is ready; only
  configuration and a Redis presence implementation are missing). Rate limiting is per instance.
- Each tenant host needs its own App Service certificate binding (no wildcard certificates in App Service).

## Deviations from PLAN.md

- Request validation is explicit (`ValidationErrors` helper) rather than .NET 10's generated minimal-API
  validation, to keep behaviour obvious and testable.
- The Notifications module has no tables (no outbox of its own); its handlers run from the publishing
  module's outbox.
- Frontend API types are hand-written (`web/src/api/*.ts`); generating them from OpenAPI is in TODO.
- Platform admin endpoints for tenants live in the Tenants module; admin invitations/passwords in Identity.
