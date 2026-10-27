# Fidoco Live

Multi-tenant platform for paid live-streamed events. Organizers (tenants) sell access to live
streams and replays; Fidoco sells in its own name (merchant of record) and settles with the organizer.
First tenant: Apex Fight Night (MMA, ~400 buyers × 199 kr per gala).

- Backend: ASP.NET Core (.NET 10), modular monolith, EF Core (SQL Server in production, SQLite locally and in tests)
- Frontend: React + TypeScript (Vite), one SPA for viewers, organizer admin and platform admin, served by the backend
- Video: Mux (live ingest, transcoding, CDN, recording, signed playback) · Payments: Stripe Checkout · E-mail: SMTP

See [ARCHITECTURE.md](ARCHITECTURE.md) for the design, [PLAN.md](PLAN.md) for the approved plan and
decisions, and [TODO.md](TODO.md) for what is deliberately left for later.

---

## Run locally

Prerequisites: .NET 10 SDK, Node.js 22. Docker is optional (SQL Server tests).

```bash
# 1. Backend (http://localhost:5080). Development uses SQLite (src/Fidoco.Host/App_Data),
#    fake Stripe/Mux, e-mails written to files and seeds demo data.
cd src/Fidoco.Host
dotnet run

# 2. Frontend (http://*.localhost:5173, proxies /api, /files and /hubs to the backend)
cd web
npm ci
npm run dev
```

| What | URL | Sign in |
|---|---|---|
| Viewer site (Apex Fight Night) | http://afn.localhost:5173 | buy a ticket, then the magic link |
| Organizer admin | http://afn.localhost:5173/admin | `admin@afn.local` / `Fidoco-dev-123!` |
| Platform admin | http://admin.localhost:5173 | `admin@fidoco.local` / `Fidoco-dev-123!` |

`*.localhost` resolves to 127.0.0.1 in all modern browsers; the tenant is resolved from the host name.

Development helpers (only with the fake providers, never in production):

- **Fake checkout**: "Köp" opens a local payment page; paying runs the same webhook processing as Stripe.
- **E-mails** (receipts, login links, invitations) are logged and saved as HTML in `src/Fidoco.Host/App_Data/emails/`.
- **Simulated encoder**: on an event's admin page, "Starta signal"/"Stoppa signal" makes the stream active/idle,
  so live → ended → replay can be tried without Mux. The player shows a placeholder for fake playback ids.

Reset the local database: stop the backend and delete `src/Fidoco.Host/App_Data/`. The SQLite schema is
created from the model at startup (migrations are for SQL Server).

### Tests

```bash
dotnet test Fidoco.Live.slnx --filter "Category!=SqlServer"   # unit, integration (SQLite) and architecture tests
cd web && npm run lint && npm run typecheck && npm test        # frontend

# Migrations + end-to-end smoke test against SQL Server (as in CI):
docker run -d --name fidoco-mssql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Fidoco_Ci_Passw0rd!' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
FIDOCO_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=Fidoco_Ci_Passw0rd!;TrustServerCertificate=True' \
  dotnet test tests/Fidoco.IntegrationTests --filter "Category=SqlServer"
```

No test needs network access: Stripe, Mux and e-mail are faked; Stripe and Mux webhook signatures are
generated locally and verified with the real code.

### Migrations

Each module has its own `DbContext`, schema and migrations (SQL Server):

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Modules/Fidoco.Modules.<Module> -s src/Fidoco.Host -c <Module>DbContext -o Infrastructure/Migrations
```

A test fails if a model change has no migration.

---

## Configuration

All settings come from `appsettings.json`, user secrets (`dotnet user-secrets` in `src/Fidoco.Host`) and
environment variables (`Section__Key`, e.g. `Stripe__SecretKey`). Never put secrets in the repository.

| Setting | Purpose |
|---|---|
| `Database:Provider` | `SqlServer` (production) or `Sqlite` (development) |
| `Database:ConnectionString` | Azure SQL connection string |
| `Database:MigrateOnStartup` | `true` applies all module migrations at startup (single instance) |
| `Platform:Hosts` | Platform host(s) without a tenant, e.g. `admin.fidoco.se` (platform admin, webhooks) |
| `Platform:TenantDomainSuffix` | New tenants get `{slug}.{suffix}`, e.g. `fidoco.se` |
| `Platform:BootstrapAdminEmail` | Creates the first platform admin and e-mails an invitation (when none exists) |
| `Platform:Seller:LegalName`, `OrganizationNumber`, `VatNumber`, `Address` | Seller shown on receipts (Fidoco) |
| `PublicUrls:Template` | Absolute link template, `https://{host}` in production |
| `Stripe:SecretKey`, `Stripe:WebhookSecret` | Stripe API key and webhook signing secret |
| `Payments:Provider` | `Stripe` (or `Fake` in Development only) |
| `Mux:TokenId`, `Mux:TokenSecret` | Mux API access token (Mux Video read/write) |
| `Mux:WebhookSecret` | Mux webhook signing secret |
| `Mux:SigningKeyId`, `Mux:SigningKeyPrivate` | Mux signing key for signed playback (base64 private key as Mux returns it) |
| `Mux:LatencyMode`, `Mux:ReconnectWindowSeconds` | `standard` and `300` by default |
| `Video:Provider` | `Mux` (or `Fake` in Development only) |
| `Email:Provider` | `Smtp` (or `File` in Development only) |
| `Email:Smtp:Host`, `Port`, `Username`, `Password`, `UseStartTls` | SMTP server (e.g. Azure Communication Services Email) |
| `Email:FromAddress`, `Email:FromDisplaySuffix` | `noreply@fidoco.se`, `" via Fidoco"` |
| `Storage:LocalPath` | Uploaded images; on App Service use `/home/data/files` |
| `RateLimiting:*PerMinute` | Login, checkout, playback token and heartbeat limits |
| `Playback:HeartbeatSeconds`, `SessionTimeoutSeconds`, `TokenLifetime` | 30, 75, 15 minutes |
| `Azure:SignalR:ConnectionString` | Enables Azure SignalR Service for the (still empty) realtime hub |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Sends traces, metrics and logs to Application Insights |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Alternatively, any OpenTelemetry collector |

The app refuses to start outside Development with fake providers or missing Mux signing keys.

---

## Stripe

1. **Keys**: Developers → API keys. Use the test-mode `sk_test_...` until launch.
2. **Webhook endpoint**: Developers → Webhooks → Add endpoint
   `https://admin.fidoco.se/api/v1/webhooks/stripe` with the events
   `checkout.session.completed`, `checkout.session.async_payment_succeeded`,
   `checkout.session.async_payment_failed`, `checkout.session.expired` and `charge.refunded`.
   Put its signing secret in `Stripe:WebhookSecret`.
3. **Payment methods**: Settings → Payment methods. Checkout uses the methods enabled there, so
   **Swish** is enabled in the Dashboard (when available for the account) without code changes.
4. **Terms**: Settings → Checkout: link to `https://<tenant host>/kopvillkor` if you require consent.
5. **Refunds** are made in the Dashboard; the `charge.refunded` webhook revokes the buyer's access.

Locally with real Stripe test mode (instead of the fake provider): set `Payments:Provider=Stripe` and the
keys, then forward webhooks with the Stripe CLI:

```bash
stripe listen --forward-to localhost:5080/api/v1/webhooks/stripe   # prints the whsec_... to use
```

## Mux

1. **Access token**: Settings → Access Tokens → new token with *Mux Video* read and write →
   `Mux:TokenId` / `Mux:TokenSecret`.
2. **Signing key** (signed playback): Settings → Signing Keys → create → `Mux:SigningKeyId` and the
   base64 private key → `Mux:SigningKeyPrivate`. Videos are never public: live streams and recordings
   use signed playback ids, and the backend signs short-lived tokens only for buyers with a valid ticket.
3. **Webhook**: Settings → Webhooks → `https://admin.fidoco.se/api/v1/webhooks/mux` → signing secret
   in `Mux:WebhookSecret`. The app uses `video.live_stream.active`, `video.live_stream.idle`,
   `video.asset.created`, `video.asset.ready`, `video.asset.live_stream_completed` and `video.asset.errored`.
4. **Live stream**: created by the organizer ("Skapa sändning" in the admin); one per event. If the
   encoder is away longer than the reconnect window (300 s) the stream goes idle and the event ends; it
   goes live again if the encoder comes back. The admin's status polling also repairs missed webhooks.

Mux cannot reach `localhost`; for local tests with real Mux use a tunnel (e.g. ngrok) for the webhook,
or rely on the admin status polling.

## Connect vMix or OBS

The event's admin page shows **Server (RTMP)**, **Server (RTMPS)** and the **stream key**
(hidden until "Visa"; "Byt strömnyckel" invalidates the old key).

- **vMix**: click the cog next to **Stream** → Destination *Custom RTMP Server*.
  URL = the RTMPS server (`rtmps://global-live.mux.com:443/app`, or RTMP `rtmp://global-live.mux.com:5222/app`),
  Stream Name or Key = the stream key. Start streaming.
- **OBS**: Settings → Stream → Service *Custom...* → Server = the RTMPS/RTMP server, Stream Key = the key.
  Settings → Output: H.264, rate control CBR, **keyframe interval 2 s**; e.g. 1080p30 at 4 500–6 000 kbps
  (720p30 at 2 500–4 000 kbps over weaker uplinks), AAC 128–160 kbps, 48 kHz.

Start the encoder 10–15 minutes before the event to verify the status turns to "Tar emot signal". The event
switches to live automatically; viewers' pages switch from the countdown to the player within 15 seconds.
Keep the stream key secret: anyone with it can broadcast on the event.

---

## Deploy to Azure (App Service + Azure SQL)

### Database: use the DTU model (Basic or S0), not serverless

The outbox processors poll the database every few seconds. A **serverless** database therefore never
auto-pauses and is billed for continuous vCore time, and on the **free offer** the monthly free quota runs
out; when it does, the free-offer database is **paused for the rest of the month** (the default
behaviour) and the platform stops working. Choose **Basic** (≈5 DTU) or **S0** (10 DTU) for a fixed,
predictable price; scale up the tier before large events if needed.

### Steps

```bash
RG=fidoco-prod; LOC=swedencentral; APP=fidoco-live; SQL=fidoco-sql
az group create -n $RG -l $LOC
az sql server create -g $RG -n $SQL -l $LOC --admin-user fidocoadmin --admin-password '<strong password>'
az sql db create -g $RG -s $SQL -n fidoco --edition Basic          # or: --service-objective S0
az sql server firewall-rule create -g $RG -s $SQL -n azure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az appservice plan create -g $RG -n fidoco-plan --is-linux --sku B1
az webapp create -g $RG -p fidoco-plan -n $APP --runtime "DOTNETCORE:10.0"
az webapp config set -g $RG -n $APP --always-on true            # required: outbox and retention jobs
```

App settings (Configuration → Application settings; use Key Vault references for secrets):

```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true       # correct https scheme and client IP behind App Service
Database__Provider=SqlServer
Database__ConnectionString=<Azure SQL connection string>
Database__MigrateOnStartup=true
Platform__Hosts__0=admin.fidoco.se
Platform__BootstrapAdminEmail=<your e-mail>
Platform__Seller__LegalName=..., Platform__Seller__OrganizationNumber=..., Platform__Seller__VatNumber=...
Storage__LocalPath=/home/data/files
Email__Smtp__Host=..., Email__Smtp__Username=..., Email__Smtp__Password=...
Stripe__SecretKey=..., Stripe__WebhookSecret=...
Mux__TokenId=..., Mux__TokenSecret=..., Mux__WebhookSecret=..., Mux__SigningKeyId=..., Mux__SigningKeyPrivate=...
APPLICATIONINSIGHTS_CONNECTION_STRING=...
```

Publish (builds the React app into `wwwroot` automatically):

```bash
dotnet publish src/Fidoco.Host -c Release -o publish
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy -g $RG -n $APP --src-path app.zip --type zip
```

Health checks: `/health/live` (process) for the App Service health check, `/health` (JSON with databases,
outbox, Stripe and Mux; external problems report *Degraded*).

### Domains and certificates

- DNS for `fidoco.se`: `admin.fidoco.se` and each tenant `{slug}.fidoco.se` as CNAME to `<app>.azurewebsites.net`
  plus the `asuid` TXT record, then App Service → Custom domains → add, and **App Service Managed Certificate**
  per host name (free; wildcards are not supported, so one binding per tenant host in phase 1).
- A tenant's own domain (e.g. `stream.afnmma.se`): the organizer adds the CNAME/TXT, you bind it with a
  managed certificate, then add it under the tenant in the platform admin.
- E-mail: verify `fidoco.se` in the e-mail provider and publish SPF, DKIM and DMARC records.

### Scaling out

One instance handles thousands of concurrent viewers (video goes through Mux's CDN; heartbeats only touch the
cache). Before running **more than one instance**: switch `IDistributedCache` to Redis and implement
`IViewerPresence` on Redis (see TODO.md); rate limits are per instance.
