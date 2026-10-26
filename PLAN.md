# Fidoco Live – Plan

Status: **Förslag, väntar på godkännande.** Reviderad 2026-10-26 med beslut 1, 2, 11, 13 och 14 (se §13a). Ingen implementationskod skrivs innan planen är godkänd.

Fidoco Live är en multi-tenant-plattform för betalda livesända event. Första tenant är Apex Fight
Night (AFN): cirka 400 köpare per gala à 199 kr. Plattformen ska kunna växa till andra arrangörer
och eventtyper och få rikare interaktiva funktioner, utan omskrivning.

Planen beskriver vad som byggs i fas 1 och var framtida funktioner ska kopplas in. Avsnitt 13
listar beslut som du behöver ta ställning till. Varje punkt har en rekommendation, så att det
räcker att svara "ok" eller ange avvikelser.

---

## 1. Principer (styr alla val nedan)

- **En app, en driftsättning.** Modulär monolit. Inga mikrotjänster, ingen MediatR, inga generiska
  repositories. Endpoints anropar EF Core direkt eller en liten handler-klass i samma fil.
- **Dörrar hålls öppna med gränssnitt, inte med färdig infrastruktur.** Exempel: `IDistributedCache`
  (minne → Redis), `IViewerPresence`, `IPaymentGateway`, `IVideoProvider`, `IEmailSender`,
  `IFileStorage` och `ITicketAvailability`. Varje gränssnitt har en implementation i fas 1 och en
  plats där nästa implementation ska in.
- **Branschneutral kärna.** Kärnan heter `Event`, `Stream`, `TicketType`, `Purchase` och
  `Entitlement`. Kampsportsbegrepp hör hemma i en vertikal modul ovanpå kärnan.
- **Backend äger alla regler.** Frontend visar tillstånd och skickar avsikter. Exempel: om en
  tittare får se repris avgörs på servern.
- **Engelska i kod, namn och kommentarer.** Svenska UI-texter i i18n-filer. API:et returnerar
  felkoder, inte texter, och frontend översätter koderna.

---

## 2. Arkitekturöversikt

```
                    ┌─────────────────────────────────────────────────────────────┐
 Browser (SPA) ───► │ Fidoco.Host  (ASP.NET Core, .NET 10)                         │
 stream.afnmma.se   │  • Tenant resolution (Host header)  • Auth (cookies)         │
                    │  • /api/v1/*  (Minimal APIs, per module)  • /hubs/live (tom) │
                    │  • SPA static files + OG-tag injection  • /health            │
                    │  • Outbox processors (BackgroundService)                     │
                    │                                                              │
                    │  Modules (one project each, reference ONLY Fidoco.Contracts) │
                    │  ┌─────────┐┌────────┐┌────────┐┌────────┐┌────────┐┌──────┐│
                    │  │Identity ││Tenants ││Catalog ││Payments││Playback││Notif.││
                    │  └─────────┘└────────┘└────────┘└────────┘└────────┘└──────┘│
                    │       ▲ public interfaces + domain events in Fidoco.Contracts│
                    └───────┼──────────────────────────────────────────────────────┘
                            │
   Stripe (Checkout, webhooks) · Mux (live, assets, signed playback, webhooks) · SMTP
   SQL Server (prod) / SQLite (dev, test) — one schema per module
```

### Modulgränser – regler

1. Moduler refererar **aldrig** varandras projekt. Varje modul refererar bara `Fidoco.SharedKernel`
   (teknisk grund) och `Fidoco.Contracts` (affärs-API mellan moduler).
2. `Fidoco.Contracts` innehåller per modul: publika gränssnitt (t.ex. `ICatalogQueries`),
   DTO:er och domänhändelser (t.ex. `PurchaseCompleted`). Contracts innehåller ingen logik.
3. Implementationerna av kontraktens gränssnitt ligger i ägarmodulen och registreras i DI av
   modulens egen `AddXxxModule()`.
4. Varje modul har en egen `DbContext` med eget SQL Server-schema (`identity`, `tenants`,
   `catalog`, `payments`, `playback`, `notifications`). Ingen modul läser en annan moduls tabeller.
   SQLite saknar scheman och ignorerar dem. Därför är tabellnamn unika över alla moduler, även
   outbox-tabellerna (t.ex. `PaymentsOutbox`).
5. Bara `Fidoco.Host` refererar modulprojekten, och bara för att anropa `AddXxxModule()` och
   `MapXxxEndpoints()`.
6. Ett arkitekturtest (NetArchTest) verifierar reglerna (se §10).

Upplägget gör beroendecykler omöjliga, eftersom moduler bara pekar på Contracts. Kompilatorn
stoppar dessutom de flesta brott mot reglerna innan testet behövs.

---

## 3. Projektstruktur

```
Fidoco.Live.slnx
global.json                         # pins .NET 10 SDK
Directory.Build.props               # net10.0, nullable, TreatWarningsAsErrors, analyzers
Directory.Packages.props            # central package management
src/
  Fidoco.Host/                      # Program.cs, composition, middleware, SPA hosting, OG tags,
                                    # health, OpenTelemetry, rate limiting, SignalR hub, dev seed
  Fidoco.SharedKernel/              # tenancy, ModuleDbContext base, domain events, outbox,
                                    # ProblemDetails/error codes, Money, common options
  Fidoco.Contracts/                 # Identity/, Tenants/, Catalog/, Payments/, Playback/,
                                    # Notifications/  (interfaces, DTOs, domain events)
  Modules/
    Fidoco.Modules.Identity/
    Fidoco.Modules.Tenants/
    Fidoco.Modules.Catalog/
    Fidoco.Modules.Payments/
    Fidoco.Modules.Playback/
    Fidoco.Modules.Notifications/
  Verticals/
    README.md                       # where industry modules go (empty in phase 1)
    # future: Fidoco.Verticals.CombatSports/  (fight card, bouts, results; refs Contracts only)
web/                                # React + TypeScript (Vite) SPA
  src/
    app/                            # router, providers, tenant branding/theme
    api/                            # fetch client, generated OpenAPI types, per-area calls
    i18n/locales/sv/*.json          # UI texts (sv first, more languages later)
    features/viewer/                # event page, checkout result, watch, account, login
    features/admin/                 # tenant admin
    features/platform/              # platform admin
    shared/                         # components, hooks (polling, backoff, countdown)
    verticals/                      # empty in phase 1 (e.g. combat-sports UI later)
tests/
  Fidoco.UnitTests/                 # pure logic: access policy, outbox backoff, Mux JWT, etc.
  Fidoco.IntegrationTests/          # WebApplicationFactory + SQLite, fake Stripe/Mux/email
  Fidoco.ArchitectureTests/         # module boundary rules
.github/workflows/ci.yml
README.md, ARCHITECTURE.md, TODO.md
```

### Intern struktur i en modul (exempel Catalog)

```
Fidoco.Modules.Catalog/
  CatalogModule.cs          # public: AddCatalogModule(), MapCatalogEndpoints()  (only public types)
  Domain/                   # Event, Stream, TicketType, StreamRecording (internal)
  Features/                 # vertical slices: one file per use case = endpoint + logic
    Admin/CreateEvent.cs, Admin/UpdateEvent.cs, Admin/CreateStream.cs, Admin/GetStreamStatus.cs
    Public/GetEvent.cs, Public/ListEvents.cs
    Webhooks/MuxWebhook.cs
  Integration/              # CatalogQueries : ICatalogQueries, TicketAvailability, event handlers
  Infrastructure/           # CatalogDbContext, Migrations/, Mux/MuxVideoProvider.cs
```

Allt utom `CatalogModule` är `internal`. Testprojekten får åtkomst via `InternalsVisibleTo`.

---

## 4. Moduler

| Modul | Ansvar (fas 1) | Publika kontrakt (Fidoco.Contracts) | Domänhändelser ut |
|---|---|---|---|
| **Tenants** | Tenant (namn, slug, logotyp, färger, supportadress), domäner, upplösning från host-headern, branding-API, affärsvillkor (provision), PlatformAdmins CRUD av tenants | `ITenantDirectory` (lookup by host/id, commercial terms), `TenantInfo` | `TenantCreated` |
| **Identity** | Kunder (köpare) med magic link, admin-användare (TenantAdmin) och PlatformAdmin med lösenord, cookies, Data Protection-nycklar i DB, GDPR-radering av kund | `ICustomerAccounts` (GetOrCreate by email), `ICustomerLoginLinks` (create link for receipt), `ICurrentUser` | `CustomerDeleted` |
| **Catalog** | Event, Stream (Mux live stream), TicketType, inspelningar (repris), Mux-webhooks, eventstatus | `ICatalogQueries` (event/ticket info, playback targets), `ITicketAvailability` (reservation seam) | `EventPublished`, `StreamWentLive`, `StreamWentIdle`, `EventEnded`, `RecordingReady` |
| **Payments** | Checkout (Stripe), CheckoutSession, Purchase + PurchaseLine, Stripe-webhooks, återbetalning, försäljningsstatistik, avräkning mot arrangör (§8.5) | `IPurchaseQueries` (stats, customer purchases) | `PurchaseCompleted`, `PurchaseRefunded` |
| **Playback** | Entitlements (rätt att titta), Mux-tokens, uppspelningssessioner, heartbeat, en session per biljett, aktiva tittare | `IPlaybackStats` (active viewers) | – (fas 1) |
| **Notifications** | E-postmallar (i18n, tenant-branding), utskick av kvitto och inloggningslänkar | `IEmailDispatcher` (send templated email now) | – |

**Varför Entitlement finns i Playback och inte "Purchase-kolla vid varje token":** Ett köp är en
betalningshändelse. En rätt att titta kan på sikt komma från annat håll, till exempel gratis
registrering, åtkomstkod eller en fysisk biljett med stream. Playback bygger därför sin egen
`Entitlement` från händelser: `PurchaseCompleted` skapar en, `PurchaseRefunded` tar bort den.
Det motsvarar "åtkomst efter betalning via outbox" i kravlistan. Token-endpointen behöver då bara
läsa i sina egna tabeller.

**Branschmoduler** (t.ex. `Fidoco.Verticals.CombatSports`) följer samma regler som
kärnmodulerna. De refererar bara Contracts, har eget schema (`combat`), kopplas till kärnan via
`EventId` och reagerar på kärnans händelser, till exempel `StreamWentLive` och `EventEnded`.
Frontend får en motsvarande mapp under `web/src/verticals/`, och eventsidan har en tydlig plats
(slot) där en vertikals komponent kan monteras. Inga vertikaler byggs i fas 1.

---

## 5. Tvärgående mekanismer

### 5.1 Tenancy

- **Upplösning:** En middleware slår upp `Host`-headern mot `tenants.TenantDomains` via
  `ITenantDirectory`. Resultatet cachas i minnet i 60 s per instans, vilket är ett läs-cache och
  inte sessionstillstånd. Resultatet sätts på ett scoped `ITenantContext`.
  - En plattformshost (konfig `Platform:Hosts`, t.ex. `admin.fidoco.se`) har ingen tenant och
    används för PlatformAdmin och webhooks.
  - Okänd host ger 404 för API:et och en enkel "okänd sida" i SPA:n.
  - Lokalt används `afn.localhost`, `test.localhost` och `admin.localhost`. `*.localhost` pekar
    på 127.0.0.1 i alla moderna webbläsare.
- **Isolering:** Allt tenant-data implementerar `ITenantOwned { Guid TenantId }`. Basklassen
  `ModuleDbContext` i SharedKernel:
  - lägger ett namngivet global query filter (EF Core 10 named filters) på varje `ITenantOwned`:
    `e.TenantId == tenantContext.TenantId`. Saknas tenant matchar filtret ingenting (fail closed).
  - stämplar `TenantId` på nya entiteter i `SaveChanges` och kastar ett undantag om en entitet
    skrivs med en annan tenant än den aktuella.
- **Utan HTTP-request** (webhooks, outbox) sätts tenant explicit med
  `tenantContext.Use(tenantId)`. Webhooks letar upp sin entitet med `IgnoreQueryFilters()` på
  externt id, till exempel Mux live stream id eller Stripe session id. Det är de enda godkända
  ställena för `IgnoreQueryFilters()`, och de dokumenteras i ARCHITECTURE.md.

### 5.2 Domänhändelser och outbox (en mekanism)

- Entiteter samlar händelser i `DomainEvents`. När `ModuleDbContext.SaveChanges` körs serialiseras
  varje händelse till modulens outbox-tabell **i samma transaktion**, en rad per
  (händelse × registrerad handler). Fan-out per handler gör att en handler som fallerar kan
  försöka igen utan att de andra körs om.
- `OutboxProcessor<TDbContext>` (BackgroundService, en per modul):
  - **Hämtning:** pollar var 2:a sekund, men väcks direkt efter commit via en in-process-signal.
    Ett köp ger därför åtkomst inom millisekunder i normalfallet.
  - **Låsning:** claimar rader portabelt med `ExecuteUpdate ... SET LockedBy, LockedUntil WHERE
    (LockedUntil IS NULL OR LockedUntil < now)`. Flera instanser plockar alltså aldrig samma rad,
    på både SQLite och SQL Server.
  - **Körning:** sätter tenant, skapar ett DI-scope och anropar handlern.
  - **Omförsök:** exponentiell backoff (2 s, 4 s, 8 s … max 10 min) upp till 10 försök. Därefter
    markeras raden `Failed`, loggas som error och syns i health/metrics.
- Handlers implementerar `IDomainEventHandler<T>` och måste vara idempotenta (at-least-once).
  Exempel: Entitlement har unikt index på `SourcePurchaseLineId`.
- Processade rader rensas efter 30 dagar, vilket också är bra för GDPR.
- Det finns ingen separat "osäker" in-process-buss. Alla händelser går via outboxen. Det är en
  mekanism att förstå, och latensen är försumbar.

### 5.3 Autentisering och auktorisering

Inloggning sker med cookies (HttpOnly, Secure, SameSite=Lax). En cookie gäller per host, vilket
ger naturlig separation mellan tenants. Det finns två scheman:

- **`Customer`** (köpare): magic link.
  - `POST /auth/magic-link` svarar alltid 202 och avslöjar inte om e-postadressen finns.
  - Token: 32 slumpbytes. Bara SHA-256-hash sparas, giltig 15 min, engångs.
  - Länken pekar på en **SPA-route** som POST:ar token till API:et. En GET förbrukar alltså aldrig
    token, så e-postskannrar (t.ex. Outlook Safe Links) kan inte förbruka länken.
  - Kundkonton är **per tenant**: samma e-post hos två arrangörer ger två konton.
- **`Admin`**: e-post + lösenord, hashat med `PasswordHasher<T>` från
  `Microsoft.Extensions.Identity.Core`. Hela ASP.NET Core Identity-ramverket används inte.
  - Roller: `TenantAdmin` (tabell `AdminUsers`, tenant-ägd) och `PlatformAdmin` (tabell
    `PlatformAdmins`, loggar bara in på plattformshosten).
  - Inbjudan och lösenordsåterställning går via engångslänk.
  - Första PlatformAdmin skapas från konfig `Platform:BootstrapAdminEmail` vid start, med
    inbjudningslänk via e-post.
- **CSRF:** SameSite=Lax. Muterande endpoints kräver JSON-body och headern `X-Fidoco-Client: web`.
- **Delade nycklar:** Data Protection-nycklarna lagras i DB (`identity.DataProtectionKeys`) så att
  alla instanser kan läsa cookies.
- **Policies:** `CustomerOnly`, `TenantAdmin` (kräver att claimens tenant = hostens tenant),
  `PlatformAdmin`.

### 5.4 Tillståndslöshet, cache och skalning

- Inget sessionstillstånd i minnet. Uppspelningssessioner lagras i `IDistributedCache`, som är
  `AddDistributedMemoryCache` i fas 1 och Redis senare via ändrad konfig.
- **Aktiva tittare** kan inte räknas med `IDistributedCache` (ingen räkning eller enumerering).
  Därför finns gränssnittet `IViewerPresence`: InMemory i fas 1, som är exakt vid en instans,
  och Redis senare (sorted set). Se §14.
- **Flera instanser kräver Redis.** Koden behöver inte ändras, men minnescachen delas inte mellan
  instanser. Det dokumenteras i README och TODO.
- Publika, ofta lästa svar (eventsida, eventstatus, branding) cachas med ASP.NET Core output
  caching i 5–10 s, med host + slug som nyckel.

### 5.5 Rate limiting (inbyggd `Microsoft.AspNetCore.RateLimiting`, per instans)

| Policy | Partition | Gräns (start, konfigurerbar) |
|---|---|---|
| `login` (magic link, admin login, reset) | IP + e-post | 5/min |
| `checkout` | IP | 10/min |
| `playback-token` (starta session, ny token) | kund-id | 10/min |
| `heartbeat` | session-id (inte IP, eftersom många tittare kan dela IP på en pub) | 6/min |

### 5.6 Fel, validering, i18n

- **Valideringsfel:** returneras som `ProblemDetails` med `code`, till exempel `ticket_not_on_sale`,
  `session_replaced` eller `access_expired`. Validering sker med .NET 10:s inbyggda Minimal
  API-validering (`AddValidation()` + DataAnnotations).
- **Frontend:** `react-i18next` med `sv` som standard. Felkoder översätts via `errors.<code>`.
- **E-post:** mallarna har egna språkfiler i Notifications-modulen (`sv`).

### 5.7 Observability och health

- **Loggning:** strukturerad loggning med `ILogger` (JSON-konsol i produktion). Inga
  personuppgifter i loggar: e-post maskeras och IP loggas inte.
- **OpenTelemetry:** traces, metrics och logs. Export till Azure Monitor (Application Insights)
  med `UseAzureMonitor()` när `APPLICATIONINSIGHTS_CONNECTION_STRING` finns, annars OTLP eller
  konsol.
- **Egna mätvärden:** `purchases_completed`, `playback_sessions_started`, `heartbeats`,
  `sessions_replaced`, `outbox_pending` och `outbox_failed`.
- **Health:**
  - `/health/live` kontrollerar bara processen.
  - `/health` kontrollerar DB, outbox-fel, Stripe och Mux. Stripe och Mux kontrolleras med en
    cachad check var 60:e sekund. Externa fel ger `Degraded`, inte `Unhealthy`, så att App
    Service inte startar om appen när Mux har problem.

### 5.8 Konfiguration och hemligheter

- All konfiguration hanteras med options-klasser och `ValidateOnStart()`. Hemligheter kommer från
  user-secrets lokalt och App Service-inställningar eller Key Vault-referenser i produktion.
- Fejk-implementationer av Stripe, Mux och e-post kan väljas med konfig (`Payments:Provider=Fake`
  osv.), men bara i `Development`. Startup kastar ett undantag annars. Det gör att appen kan köras
  lokalt utan konton och utan nätverk.

### 5.9 Realtid

Strukturen innehåller en tom `LiveHub` på `/hubs/live`. `AddSignalR()` körs alltid, och
`.AddAzureSignalR()` läggs till om `Azure:SignalR:ConnectionString` är satt. Inga
realtidsfunktioner byggs i fas 1. Tittarsidan pollar eventstatus var 15:e sekund (output-cachat).

---

## 6. Datamodell

Konventioner:
- Id är `Guid` v7 (`Guid.CreateVersion7()`, sorterbart).
- Tider lagras som `DateTimeOffset` i UTC. `TimeProvider` används för testbarhet.
- Belopp lagras i minor units (öre) som `long` + `Currency` (ISO, `SEK` i fas 1).
- **Fetstil** = `ITenantOwned`, med TenantId och global query filter.

### tenants
- `Tenants`: Id, Name, Slug (unik), LogoUrl?, PrimaryColor, AccentColor, SupportEmail,
  Status (Active/Suspended), CreatedAt. *(Tenant-tabellen är plattformsdata. Den är inte själv
  tenant-filtrerad.)*
  - Affärsvillkor (sätts av PlatformAdmin, se §8.5): CommissionPercent (provision i % av intäkt
    exkl. moms), CommissionFixedAmount (öre exkl. moms per biljett, standard 0),
    OrganizerVatRegistered (bool, styr om arrangörens fakturaunderlag ska ha moms).
- `TenantDomains`: Id, TenantId, Host (unik, lowercase), IsPrimary.

### identity
- **`Customers`**: Id, TenantId, Email (normaliserad, unik per tenant), CreatedAt, LastLoginAt?
- **`LoginTokens`**: Id, TenantId, CustomerId, TokenHash (unik), Purpose (Login/Receipt), ExpiresAt,
  ConsumedAt?, CreatedAt.
- **`AdminUsers`**: Id, TenantId, Email, PasswordHash?, Role (`TenantAdmin`), CreatedAt,
  LastLoginAt?
- `PlatformAdmins`: Id, Email, PasswordHash?, CreatedAt, LastLoginAt?
- `AdminTokens`: Id, AdminKind (Tenant/Platform), AdminId, TokenHash, Purpose (Invite/Reset),
  ExpiresAt, ConsumedAt?
- `DataProtectionKeys`: standardtabell.

### catalog
- **`Events`**: Id, TenantId, Slug (unik per tenant), Title, Description, ImageUrl?, StartsAt,
  Status (`Draft`/`Scheduled`/`Live`/`Ended`), LiveStartedAt?, EndedAt?, CreatedAt, UpdatedAt.
- **`Streams`**: Id, TenantId, EventId, Name, SortOrder.
  - Mux-kopplingar: Provider (`Mux`), ProviderStreamId (Mux live stream id, unik),
    LivePlaybackId (signed).
  - Status: `Provisioning`/`Idle`/`Active`/`Disabled`, StatusChangedAt (för att ignorera
    webhooks som kommer i fel ordning).
  - LatencyMode.
  - Stream key **lagras inte**. Den hämtas från Mux när admin visar den.
  - *Flera streams per event stöds av modellen. Fas 1 skapar en.*
- **`StreamRecordings`**: Id, TenantId, StreamId, ProviderAssetId (unik), PlaybackId (signed),
  DurationSeconds?, Status (`Preparing`/`Ready`/`Errored`), IsPrimary, CreatedAt. En Mux-stream
  kan ge flera assets om encodern kopplar ner längre än reconnect window. Repris använder
  `IsPrimary`: som standard den längsta färdiga, och admin kan byta.
- **`TicketTypes`**: Id, TenantId, EventId, Name, SortOrder.
  - Typ: Kind (`DigitalAccess`/`PhysicalAdmission`), AccessMode (`Paid`; senare `FreeRegistration`,
    `AccessCode`).
  - Pris: PriceAmount (öre, inkl. moms), Currency, VatRatePercent (25).
  - Åtkomst: IncludesLive (bool), ReplayAccessDays? (null = ingen repris).
  - Försäljning: Capacity? (null = obegränsat), SalesStartsAt?, SalesEndsAt?, IsActive.
  - *Fas 1: en DigitalAccess/Paid utan Capacity per event. En biljett ger åtkomst till alla
    eventets streams. Om en biljett senare ska begränsas till vissa streams tillkommer tabellen
    `TicketTypeStreams` utan att befintliga data påverkas.*
- **`ProcessedWebhookEvents`** (catalog): Provider, EventId (PK), ProcessedAt.
- **`CatalogOutbox`**.

### payments
- **`CheckoutSessions`**: Id, TenantId, CustomerId?, Email?, ProviderSessionId (unik), Status
  (`Pending`/`Completed`/`Expired`/`Failed`), BrowserNonceHash?, ReservationId?, ExpiresAt,
  CreatedAt, CompletedAt?.
- **`Purchases`**: Id, TenantId, CheckoutSessionId (unik), CustomerId? (null efter anonymisering),
  Status (`Completed`/`Refunded`).
  - Belopp: AmountTotal, VatAmount, Currency.
  - Stripe-referenser: ProviderPaymentIntentId, ProviderChargeId?
  - Tider: CompletedAt, RefundedAt?
- **`PurchaseLines`**: Id, TenantId, PurchaseId, EventId, TicketTypeId, TicketTypeName
  (snapshot), Quantity (1 i fas 1), UnitPrice, VatRatePercent, NetAmount (exkl. moms),
  PlatformFeeAmount (Fidocos provision exkl. moms, beräknad från tenantens villkor vid köptillfället
  och aldrig omräknad i efterhand). *(Rader i stället för en enda
  TicketTypeId på köpet. Då går det att sälja fysisk biljett + stream i samma köp senare utan
  migreringsproblem.)*
- `ProcessedWebhookEvents` (payments), **`PaymentsOutbox`**.

### playback
- **`Entitlements`**: Id, TenantId, CustomerId, EventId, TicketTypeId, SourceType (`Purchase`),
  SourceId (= PurchaseLineId, unik), IncludesLive, ReplayAccessDays?, Status (`Active`/`Revoked`),
  CreatedAt, RevokedAt?
- **`PlaybackOutbox`** (tom i fas 1, finns för konsekvens).
- *Sessioner lagras **inte** i DB:*
  - `IDistributedCache` `playback:active:{entitlementId}` → `{sessionId, startedAt}`, TTL 75 s
    som förnyas av heartbeat.
  - `playback:session:{sessionId}` → `{entitlementId, eventId, customerId}`, TTL 75 s.

### notifications
- **`NotificationsOutbox`**. Mejlinnehåll lagras inte, bara outbox-raden med id:n tills den
  rensas.

### Åtkomstregel (ren funktion i Playback, enhetstestad)

```
CanWatch(entitlement, event, now):
  entitlement.Status == Active
  AND event.Status != Draft
  AND ( (mode == Live   AND entitlement.IncludesLive AND event.Status == Live)
     OR (mode == Replay AND entitlement.ReplayAccessDays != null
                        AND event.Status == Ended AND primary recording Ready
                        AND now <= event.EndedAt + ReplayAccessDays) )
```

---

## 7. API-design (`/api/v1`)

- Minimal APIs med `MapGroup("/api/v1")`. Varje modul mappar sina egna grupper.
- **Versionering:** versionen ligger i URL:en. Ett `/api/v2` blir en ny grupp, och inget extra
  paket behövs.
- **OpenAPI:** den inbyggda `Microsoft.AspNetCore.OpenApi` exponerar `/openapi/v1.json` i
  Development. TS-typer genereras med `openapi-typescript` via `npm run gen:api` och committas.

### Publikt (tenant-host)
| Metod | Route | Beskrivning |
|---|---|---|
| GET | `/tenant` | Branding: namn, logotyp, färger |
| GET | `/events` | Publicerade event (kommande, live, nyligen avslutade) |
| GET | `/events/{slug}` | Eventdetalj + biljettyper + (om inloggad) min åtkomst: `none`/`live`/`replay`/`expired` |
| GET | `/events/{slug}/status` | Lättviktig status för polling (cachad 5 s) |

### Kund-auth och konto
| Metod | Route | Beskrivning |
|---|---|---|
| POST | `/auth/magic-link` | `{email, returnUrl}` → 202 |
| POST | `/auth/verify` | `{token}` → sätter cookie |
| POST | `/auth/logout` | |
| GET | `/me` | E-post |
| GET | `/me/purchases` | Mina köp + åtkomst |
| DELETE | `/me` | GDPR-radering (kräver bekräftelse i body) |

### Köp
| Metod | Route | Beskrivning |
|---|---|---|
| POST | `/checkout` | `{ticketTypeId}` → `{checkoutId, redirectUrl}`; 409 `already_purchased` om inloggad med åtkomst |
| GET | `/checkout/{checkoutId}` | `pending`/`completed`/`expired`. Loggar in direkt om browser-nonce matchar (se §8.1) |

### Uppspelning (kräver `Customer`)
| Metod | Route | Beskrivning |
|---|---|---|
| POST | `/events/{eventId}/playback-sessions` | Startar session (tar över ev. tidigare) → `{sessionId, mode, playbackId, tokens:{playback,thumbnail,storyboard}, tokenExpiresAt, heartbeatSeconds}` |
| POST | `/playback-sessions/{sessionId}/heartbeat` | 204; 409 `session_replaced`; 403 `access_expired` |
| POST | `/playback-sessions/{sessionId}/tokens` | Nya tokens om sessionen fortfarande är aktiv (används vid återhämtning) |
| DELETE | `/playback-sessions/{sessionId}` | Frigör sessionen (anropas med `sendBeacon` när sidan lämnas) |

### Webhooks (plattformshost; signaturverifierade; idempotenta)
| Metod | Route |
|---|---|
| POST | `/webhooks/stripe` |
| POST | `/webhooks/mux` |

### Admin (TenantAdmin, tenant-host)
| Metod | Route | Beskrivning |
|---|---|---|
| POST | `/admin/auth/login`, `/admin/auth/logout`, `/admin/auth/reset-request`, `/admin/auth/reset` | |
| GET/POST | `/admin/events` | Lista (med försäljningssiffror) / skapa (inkl. biljettyp) |
| GET/PUT | `/admin/events/{id}` | Läs / redigera (titel, beskrivning, bild, starttid, biljettyp) |
| POST | `/admin/events/{id}/publish`, `/admin/events/{id}/end` | Statusbyten (Draft→Scheduled, manuellt avsluta) |
| POST | `/admin/events/{id}/streams` | Skapar Mux live stream |
| GET | `/admin/streams/{id}` | RTMP(S)-URL, stream key (hämtas från Mux), status |
| GET | `/admin/streams/{id}/status` | Pollas var 10:e s: `idle`/`active` (Mux, cachat 5 s) |
| POST | `/admin/streams/{id}/reset-key` | Ny stream key |
| PUT | `/admin/streams/{id}/primary-recording` | Välj inspelning för repris |
| GET | `/admin/events/{id}/sales` | Antal köp, intäkt (Payments) |
| GET | `/admin/events/{id}/settlement` | Avräkning (§8.5), även `?format=csv` |
| GET | `/admin/events/{id}/viewers` | Aktiva tittare just nu (Playback) |
| PUT | `/admin/branding` | Logotyp, färger |
| POST | `/admin/uploads` | Bilduppladdning (logotyp, eventbild) → URL |
| POST | `/admin/customers/delete` | GDPR-radering av köpare via e-post (supportärende) |

### Plattform (PlatformAdmin, plattformshost)
| Metod | Route | Beskrivning |
|---|---|---|
| POST | `/platform/auth/login` m.fl. | |
| GET/POST | `/platform/tenants` | Lista / skapa |
| PUT | `/platform/tenants/{id}` | Redigera, suspendera |
| POST/DELETE | `/platform/tenants/{id}/domains` | Domäner |
| POST | `/platform/tenants/{id}/admins` | Bjud in TenantAdmin |
| PUT | `/platform/tenants/{id}/terms` | Affärsvillkor (provision) |
| GET | `/platform/settlements?from=&to=` | Avräkning för alla tenants och event, även CSV |

### Övrigt (utanför `/api`)
- `/health/live` och `/health`.
- `/hubs/live`.
- SPA fallback till `index.html`. För `/e/{slug}` och `/` injicerar servern Open Graph-taggar
  (`og:title`, `og:description`, `og:image`, `og:url`, `twitter:card`) i en platshållare i
  `index.html`, HTML-encodat och cachat. Sociala crawlers kör inte JS, så det måste göras på
  servern.

---

## 8. Nyckelflöden

### 8.1 Köp
1. Eventsidan → `POST /checkout {ticketTypeId}`.
2. Payments kontrollerar köpbarheten via `ICatalogQueries.GetTicketForPurchase()`: publicerat,
   aktivt, inom säljfönster, rätt valuta.
3. **Reservationssöm:** `ITicketAvailability.TryReserve(ticketTypeId, qty, expiresAt)` anropas.
   Catalog implementerar den. Fas 1 returnerar alltid ok utan att spara något när
   `Capacity == null`. Senare: reservationstabell + kapacitetskontroll i samma metod, utan
   ändring i Payments.
4. `CheckoutSession` (Pending) skapas.
   - En HttpOnly-cookie med browser-nonce sätts, och bara hashen sparas.
   - Stripe Checkout Session skapas med metadata `{tenantId, checkoutId}`, `expires_at` = 30 min,
     samma som reservationen, och `customer_email` om kunden är inloggad.
   - Betalmetoder styrs från Stripe Dashboard (dynamic payment methods), så Swish aktiveras
     där utan kodändring.
5. Kunden betalar och kommer tillbaka till `/e/{slug}/tack?c={checkoutId}`, som pollar
   `GET /checkout/{id}`.
6. **Webhook** `checkout.session.completed` med `payment_status=paid`, eller
   `checkout.session.async_payment_succeeded`:
   1. Signaturen verifieras med Stripe.net.
   2. Dubbletter stoppas: `ProcessedWebhookEvents` har PK på event-id, och `Purchases` har unikt
      index på CheckoutSessionId. Ett skydd för omleverans och ett för olika events om samma
      session.
   3. `ICustomerAccounts.GetOrCreate(email)` anropas. Operationen är idempotent.
   4. I **en transaktion** skapas Purchase + PurchaseLine, CheckoutSession sätts till Completed,
      reservationen bekräftas, och outbox-rader för `PurchaseCompleted` skrivs.
7. **Outbox:**
   - Playback skapar en `Entitlement`.
   - Notifications skickar kvittot. Säljare är Fidoco (juridiskt namn, org.nr och momsreg.nr
     från konfig `Platform:Seller`), och arrangören anges som "Arrangör: <tenant>". Kvittot
     innehåller pris, moms och en magic link som skapas *vid sändning* via `ICustomerLoginLinks`, så ingen rå token ligger i outboxen.
8. Tack-sidan ser `completed`. Om browser-noncen matchar loggas kunden in direkt (engångs, inom
   1 h) och skickas till visningssidan. Annars visas "kolla din e-post".
9. **Övriga webhooks:**
   - `checkout.session.expired` → Expired + `Release` av reservationen.
   - `charge.refunded` → Purchase Refunded → `PurchaseRefunded` → Entitlement Revoked.

### 8.2 Uppspelning och en session per biljett
1. Visningssidan → `POST /events/{id}/playback-sessions`.
2. Playback läser kundens Entitlement för eventet och eventets status, sluttid och playback-id:n
   via `ICatalogQueries`, cachat 10 s.
3. Åtkomstregeln (§6) avgör läget, `live` eller `replay`, eller nekar med `not_started`,
   `access_expired` eller `no_ticket`.
4. Nytt `sessionId` skapas och `playback:active:{entitlementId}` skrivs över. En ev. tidigare
   session är därmed utkastad.
5. Mux-JWT:er signeras med RS256, signing key från konfig och `kid`:
   - `aud` = `v`, `t` och `s`.
   - `sub` = playback-id.
   - Livslängd 15 min (konfig).
   - Signeringen är lokal kryptografi, så inget nätverksanrop görs.
6. Spelaren skickar heartbeat var 30:e sekund:
   - Om `active:{entitlementId} == sessionId` förnyas TTL, presence registreras och svaret är 204.
   - Annars svarar servern 409 `session_replaced`. Spelaren stoppas och visar "Din biljett används
     nu på en annan enhet" med knappen "Titta här i stället", som startar en ny session.
   - Heartbeat rör inte databasen.
7. **Felhantering i klienten:**
   - Nätverksfel och 5xx på heartbeat eller tokens ger nytt försök med exponentiell backoff
     (1, 2, 4 … max 30 s) och jitter. Uppspelningen fortsätter under tiden.
   - Bara 409, 401 och 403 stoppar.
   - Om spelaren får ett tokenfel (t.ex. vid återanslutning efter långt avbrott) hämtas nya
     tokens via `/tokens` med samma backoff.

### 8.3 Mux-status och repris
- Webhook `video.live_stream.active` → Stream `Active`, event `Live` (från Scheduled eller Ended)
  → `StreamWentLive`.
- `video.live_stream.idle` → Stream `Idle`. Om eventet var `Live` och ingen annan stream i eventet
  är aktiv: event `Ended`, `EndedAt=now` → `EventEnded`. Går streamen aktiv igen blir eventet
  `Live` igen. Repris-fönstret räknas från senaste `EndedAt`.
- `video.asset.created`/`video.asset.ready` med `live_stream_id` → `StreamRecording`. Primär sätts
  till den längsta färdiga. → `RecordingReady`.
- Signatur (`Mux-Signature`, HMAC-SHA256 med tidsstämpel) verifieras, och event-id dedupliceras.
  Uppdateringar äldre än `StatusChangedAt` ignoreras.
- Mux live stream skapas med `playback_policy=signed`, `new_asset_settings.playback_policy=signed`
  och `reconnect_window` samt `latency_mode` från konfig (se beslut 7–8).

### 8.4 GDPR-radering
`DELETE /me` eller admin/plattform → Identity raderar kund och tokens → `CustomerDeleted`:
- **Payments:** anonymiserar köp. CustomerId och e-post nollställs, belopp och datum behålls för
  bokföringen (se beslut 11).
- **Playback:** raderar entitlements. Aktiva sessioner dör vid nästa heartbeat.
- **Cookie:** rensas.
- **Övrig data:** processade outbox-rader och webhook-dedup rensas efter 30 dagar. Ej förbrukade
  login-tokens rensas efter utgång. En daglig `CleanupService` sköter det.

### 8.5 Affärsmodell och avräkning (beslut 1)
- **Säljare:** Fidoco säljer streamen i eget namn (merchant of record). Köparen betalar till
  Fidocos Stripe-konto, och Fidoco redovisar momsen (25 %) på hela priset.
- **Arrangörens andel:** arrangören fakturerar Fidoco för sin andel enligt en avräkningsrapport.
  Fidoco behåller provisionen. Stripe-avgifterna bärs av Fidoco och ska alltså täckas av
  provisionen.
- **Provision per tenant:** `CommissionPercent` av intäkten exkl. moms, plus ett valfritt fast
  belopp per biljett (`CommissionFixedAmount`). Provisionen beräknas och sparas på varje
  `PurchaseLine` vid köpet, så ändrade villkor påverkar aldrig gamla köp.
- **Återbetalning:** ett återbetalt köp ger 0 kr för både arrangör och Fidoco i avräkningen.
- **Avräkningsrapport per event**, för TenantAdmin (egna event) och PlatformAdmin (alla), med
  CSV-export:
  - antal sålda och antal återbetalda biljetter
  - brutto inkl. moms, moms och netto exkl. moms
  - Fidocos provision
  - arrangörens andel exkl. moms, plus moms om `OrganizerVatRegistered`
- Rapporten är fakturaunderlaget för arrangören. Självfakturering (Fidoco skapar arrangörens
  faktura) och Stripe Connect ligger i TODO.
- **Räkneexempel** (400 × 199 kr, 15 % provision; procentsatsen är bara ett exempel):

  | Post | Belopp |
  |---|---|
  | Brutto inkl. moms | 79 600 kr |
  | Moms (Fidoco redovisar) | 15 920 kr |
  | Netto exkl. moms | 63 680 kr |
  | Fidocos provision | 9 552 kr |
  | Arrangörens andel | 54 128 kr + 13 532 kr moms = 67 660 kr fakturerat |

  Fidoco drar av arrangörens moms som ingående moms. Stripe-avgiften (ca 1,5 % + 1,80 kr per
  europeiskt kort; kontrollera aktuell prislista) dras från Fidocos provision, ca 1 900 kr i
  exemplet.

---

## 9. Frontend

- **Stack:** Vite + React + TypeScript, `react-router`, TanStack Query (polling via
  `refetchInterval` och retry), `react-i18next` och `@mux/mux-player-react`. Ingen UI-ramverk-
  dependency; vanlig CSS med CSS-variabler för tenantens färger. Mobilen först.
- **Tittare (tenant-host):**
  - `/`: tenantens event.
  - `/e/:slug`: info + köp, eller "Titta" om man har åtkomst.
  - `/e/:slug/tack`: väntar på köpet och loggar in direkt.
  - `/e/:slug/titta`: visningssidan.
  - `/logga-in`, `/auth/verifiera` och `/konto` (mina köp, radera mina uppgifter).
  - Svenska route-namn är UI. De kan bytas senare med i18n-routes vid behov.
- **Visningssidan** har tydliga lägen:
  - nedräkning (inte startat)
  - live
  - väntar på signal (live men streamen inte aktiv)
  - avslutat: repris tillgänglig, repris förbereds, ingen repris eller repris utgången
  - ersatt av annan enhet
  - ingen biljett
- **Admin (`/admin/*` på tenant-host):**
  - inloggning
  - eventlista med köp, intäkt och tittare
  - skapa och redigera event inkl. biljettyp
  - streampanel med RTMP(S)-URL, stream key (dold tills man klickar, med kopieringsknapp),
    status som pollas var 10:e s, återställ nyckel och val av inspelning
  - branding
- **Plattform (`/platform/*` på plattformshost):** inloggning, tenants, domäner och inbjudan av
  admins.
- **Hosting:**
  - **Bygge:** `npm run build` → `web/dist` kopieras till `Fidoco.Host/wwwroot` av ett
    MSBuild-target vid `dotnet publish`, och i CI.
  - **Lokalt:** Vite dev server med proxy `/api` och `/hubs` → Kestrel med bevarad `Host`-header,
    så att tenant-upplösningen fungerar på `afn.localhost:5173`.
- **Testbar logik i hooks och moduler:**
  - `api/client.ts`: felmappning ProblemDetails → kod.
  - `shared/backoff.ts`
  - `usePlaybackSession`: start, heartbeat-intervall, backoff, stopp vid 409/403, tokenförnyelse.
  - `useCountdown`

---

## 10. Teststrategi

- **Unit (xUnit):**
  - åtkomstregeln
  - outbox-backoffberäkning
  - Mux-JWT-signering (verifieras mot publik nyckel)
  - Mux- och Stripe-signaturverifiering (vi signerar själva med testhemlighet, så inget nätverk
    behövs)
  - tenant-upplösning
  - OG-taggar (HTML-encoding)
- **Integration** (WebApplicationFactory + SQLite in-memory med delad öppen anslutning, fejkade
  `IPaymentGateway`, `IVideoProvider` och `IEmailSender`, `FakeTimeProvider`). Varje test väljer
  tenant via `Host`-headern. Testerna täcker särskilt:
  - **Webhook-idempotens:** samma Stripe-event två gånger och två olika events för samma session
    ger exakt ett köp, en entitlement och ett kvitto. Ogiltig signatur ger 400 och inga sidoeffekter.
  - **Outbox-omförsök:** en handler som kastar de två första gångerna lyckas på tredje, med korrekt
    `NextAttemptAt`. Efter max antal försök blir raden `Failed`. En fallerande handler kör inte om
    de andra. Två samtidiga processorer tar inte samma rad.
  - **Token bara med giltig biljett:**
    - utloggad → 401
    - inloggad utan köp → 403 `no_ticket`
    - med köp → 200
    - återbetalt köp → 403
    - repris efter fönstret → 403 `access_expired`
    - köp hos tenant A används på tenant B → nekas
  - **Tenant-isolering:**
    - event i A syns inte via B:s host (404)
    - TenantAdmin i A kan inte läsa eller ändra B:s event via id
    - query filter utan tenant returnerar inget
    - skrivning med fel TenantId kastar
  - **En session per köp:**
    - session 1 startas, session 2 startas → heartbeat på 1 ger 409 `session_replaced`, på 2 ger 204
    - session utan heartbeat förbi TTL räknas inte som aktiv
    - aktiva tittare räknas rätt
  - **Mux-webhooks:** active → Live, idle → Ended, asset ready → repris, gammal webhook ignoreras.
  - **GDPR:** radering anonymiserar köp, tar bort åtkomst och kunden kan inte logga in.
- **Arkitektur (NetArchTest):**
  - modulassemblies refererar inte andra modulassemblies
  - Contracts refererar bara SharedKernel
  - SharedKernel refererar varken moduler eller Contracts
  - bara `XxxModule`-klassen är publik i en modul
  - Verticals (när de finns) refererar bara Contracts och SharedKernel
- **Migrationer:** ett test kör `HasPendingModelChanges()` för varje modul-DbContext, så att en
  glömd migrering fångas i CI.
- **Frontend (Vitest):** enligt §9. Inga omfattande UI-tester i fas 1.
- Inga tester kräver nätverk.

---

## 11. CI och drift

- **GitHub Actions** (`ci.yml`, push + PR):
  - **backend:** `setup-dotnet` 10.x → `dotnet restore`, `build -c Release`, `test`.
  - **frontend:** `setup-node` 22 → `npm ci`, `lint`, `typecheck`, `test`, `build`.
  - NuGet och npm cachas.
- **Driftsättning (README):**
  - Azure App Service (Linux, .NET 10) + Azure SQL. `Always On` måste vara på för outbox och
    städjobb.
  - Konfig via App Settings och Key Vault-referenser.
  - Migrationer körs vid start bakom flaggan `Database:MigrateOnStartup` (enkel drift med en
    instans), eller som ett separat `efbundle`-steg.
  - Domäner (se beslut 13):
    - `admin.fidoco.se` är plattformshost för PlatformAdmin och webhooks.
    - Varje tenant får `{slug}.fidoco.se`, t.ex. `afn.fidoco.se`.
    - Egna domäner som `stream.afnmma.se` pekas med CNAME mot appen och verifieras med en
      TXT-post.
    - Varje hostname binds med ett App Service Managed Certificate (gratis, manuellt steg per
      tenant i fas 1).
  - Stripe- och Mux-webhooks pekar på `https://admin.fidoco.se/api/v1/webhooks/...`.
  - E-post skickas från `noreply@fidoco.se`. SPF, DKIM och DMARC sätts upp för `fidoco.se`.
- **Databas lokalt:** SQLite-fil. Migrationer genereras för SQL Server, som är källan för
  produktion. Lokalt och i tester skapas SQLite-schemat från modellen per modul
  (`IRelationalDatabaseCreator.CreateTables()`), och dev-databasen återskapas vid modelländring
  (`--reset-db`). Se beslut 15 för alternativet med SQLite-migrationer.
- **Miljö:** Den här miljön saknar .NET SDK. Jag installerar .NET 10 SDK lokalt via
  `dotnet-install.sh` (påverkar inte repot) för att kunna bygga och köra tester.

---

## 12. Implementationsordning

Varje punkt blir en commit. Bygg och tester ska vara gröna efter varje commit.

### Fas 1 – MVP
1. **Skelett:**
   - solution, `Directory.*.props`, alla projekt (tomma moduler med `AddXxxModule`), Host med
     ProblemDetails, OpenTelemetry, `/health/live`, `/api/v1`-grupp och tom SignalR-hubb
   - Vite-app med i18n och Vitest
   - arkitekturtester och CI-workflow
2. **SharedKernel:** `ITenantContext`, `ModuleDbContext` (query filters, TenantId-stämpling,
   domänhändelser → outbox), `OutboxProcessor` med claim, backoff och fan-out per handler,
   cleanup. Tester för outbox-omförsök och tenant-filter.
3. **Tenants:** modell, host-upplösning, `GET /tenant`, dev-seed (AFN på `afn.localhost`).
4. **Notifications:** `IEmailSender` (SMTP via MailKit + dev-sender som loggar och skriver
   `.html`-filer), mallar med i18n och branding, `IEmailDispatcher`.
5. **Identity:**
   - kunder med magic link
   - admins och plattformsadmins med lösenord, inbjudan och reset
   - cookies, policies, Data Protection i DB, rate limiting
   - plattforms-endpoints för tenants och domäner
6. **Catalog – event och biljettyper:** admin CRUD, publish/end, publika endpoints,
   bilduppladdning (`IFileStorage`, lokal disk).
7. **Catalog – streams och Mux:** `IVideoProvider` (Mux HTTP), skapa stream, RTMP och key,
   statuspolling, Mux-webhooks, inspelningar, domänhändelser.
8. **Payments:** checkout, `IPaymentGateway` (Stripe.net), reservationssöm, webhooks
   (completed, async succeeded, expired, refunded), browser-nonce-inloggning, kvittohandler i
   Notifications, försäljningsstatistik, provisionsberäkning och avräkningsrapport med CSV.
9. **Playback:** entitlements från händelser, åtkomstregel, Mux-JWT, sessioner, heartbeat,
   presence, `viewers`-endpoint.
10. **Frontend – tittare:** branding och tema, eventlista och eventsida, köpflöde och tack-sida,
    login och verifiering, visningssida med alla lägen, heartbeat och backoff, konto med radering,
    sidor för köpvillkor och integritetspolicy (Fidoco som säljare, texter i i18n-filer).
11. **Frontend – admin och plattform:** login, event-CRUD, streampanel med polling, statistik,
    avräkning, branding, tenants med affärsvillkor.
12. **OG-taggar och GDPR:** server-side OG-injektion, `CustomerDeleted`-flöde i alla moduler, full
    `/health` med externa beroenden.
13. **Dokumentation och slutverifiering:** README, ARCHITECTURE.md, TODO.md. Hela bygget och alla
    tester (backend + frontend) körs gröna.

### Fas 2+ (föreslagen ordning, detaljeras i TODO.md)
1. Kampsportsmodul med live-uppdaterat matchkort via SignalR
2. Tippning och omröstningar
3. Livechatt (utvärdera färdig tjänst kontra egen)
4. Fler biljettyper och åtkomstkoder
5. Klippbibliotek
6. Rabattkoder
7. Stripe Connect för utbetalningar till arrangörer (ersätter manuell avräkning; självfakturering som mellansteg)
8. Fakturabetalning för B2B
9. Flera parallella streams per event
10. VAT-hantering för fler länder
11. Redis och Azure SignalR vid skalbehov
12. Infrastruktur som kod (Bicep)
13. Fysiska biljetter med kapacitet, reservationer, QR-biljetter och skanningsapp

---

## 13. Beslut

### 13a. Fattade beslut (reviderat 2026-10-26)

1. **Fidoco är säljare (merchant of record).**
   - Pengarna går till Fidocos Stripe-konto. Arrangören fakturerar Fidoco för sin andel enligt
     avräkningsrapporten, och Fidoco behåller en provision (§8.5).
   - Det tekniskt enklaste upplägget (ett Stripe-konto) är också det juridiskt rena:
     - Stripes villkor kräver att kontoinnehavaren är den som faktiskt säljer.
     - Att i stället ta emot pengar i arrangörens namn och betala vidare liknar en betaltjänst
       enligt betaltjänstlagen och kräver tillstånd.
   - Följd: Fidoco ansvarar mot köparen för köpvillkor, reklamationer och återbetalningar. Därför
     tillkommer sidor för köpvillkor och integritetspolicy i fas 1.
   - Stripe Connect, där arrangören säljer själv och Stripe delar upp beloppet automatiskt, finns
     kvar i TODO som nästa steg när fler arrangörer ansluter.
2. **Moms:** 25 % ingår i priset (199 kr = 159,20 kr + 39,80 kr moms).
   - Fidocos juridiska uppgifter (namn, org.nr, momsreg.nr, adress) ligger i konfig
     `Platform:Seller` och står på kvittot. Arrangören anges som "Arrangör".
   - Köpare i andra EU-länder (moms i köparens land via OSS) ligger kvar i TODO.
11. **GDPR-radering:** köp anonymiseras och sparas i 7 år för bokföringen. Kopplingen till
    personen tas bort.
13. **Domäner: Fidoco äger `fidoco.se`. En `.live`-domän behövs inte.**
    - `admin.fidoco.se`: plattformsadmin och webhooks.
    - `{slug}.fidoco.se`: varje tenant, t.ex. `afn.fidoco.se`.
    - Egen domän via CNAME, t.ex. `stream.afnmma.se`.
    - `fidoco.se` (roten) är fri för en framtida marknadssida och används inte av appen i fas 1.
    - Produkten och koden heter fortfarande Fidoco (namnrymd `Fidoco.*`).
14. **E-post skickas från Fidoco för tenantens räkning.**
    - Avsändare: `"Apex Fight Night via Fidoco" <noreply@fidoco.se>`.
    - `Reply-To` = tenantens `SupportEmail`. Svar på frågor om eventet hamnar då hos arrangören,
      och det syns att Fidoco skickar.
    - Det stämmer med att Fidoco är säljare och kräver bara SPF/DKIM/DMARC för en domän.
      Att skicka från arrangörens egen domän kräver DKIM per tenant och ligger i TODO.
    - Koden använder SMTP och är leverantörsneutral. *Rek:* Azure Communication Services Email
      (samma Azure-faktura, stöder SMTP och egen domän). Postmark är ett alternativ om
      leveransen blir ett problem. Leverantören är bara konfig och kan väljas vid driftsättning.

### 13b. Kvar att ta ställning till

Fidocos företagsuppgifter (bolagsform, org.nr, momsregistrering) och provisionsnivån för AFN
behövs först vid driftsättning. Båda är konfiguration och blockerar inte implementationen.

För punkterna nedan gäller min rekommendation om du svarar "ok".

3. **Admininloggning.**
   - *Rek:* e-post + lösenord (eget minimalt, `PasswordHasher`), 2FA i TODO.
   - *Alternativ:* magic link även för admins. Enklare kod, men admin blir beroende av mejl.
4. **Magic link i kvittot.**
   - *Rek:* flergångs och giltig tills biljettens åtkomst upphör, högst 30 dagar efter eventet.
     Det är säkert nog eftersom en session per biljett gäller.
   - *Alternativ:* engångs, 24 h. Då får köparen begära en ny länk.
5. **Direktinloggning efter köp** via browser-nonce (§8.1). *Rek:* ja. Köparen kan titta direkt
   utan att öppna mejlet på samma enhet, vilket är viktigt när många köper minuterna före start.
6. **Inloggning före köp?** *Rek:* nej. Stripe samlar in e-posten och kontot skapas vid betalning.
   Inloggade köpare får e-posten förifylld. Köparen godkänner köpvillkoren i Stripe Checkout.
7. **Eventstatus vid Mux idle.**
   - *Rek:* automatiskt `Ended` vid idle, tillbaka till `Live` om streamen startar igen.
     `reconnect_window` = 300 s så att korta avbrott i encodern inte avslutar eventet.
   - *Alternativ:* bara admin avslutar manuellt.
8. **Latency mode.**
   - *Rek:* `standard` i fas 1 (stabilast, ca 20–30 s fördröjning), konfigurerbart per stream.
   - Inför tippning och live-matchkort kan `low` (ca 5 s) bli aktuellt.
9. **Flera inspelningar** (om encodern startas om). *Rek:* repris = den längsta färdiga
   inspelningen, och admin kan byta. Att slå ihop delar ligger utanför fas 1.
10. **Bildlagring.**
    - *Rek:* `IFileStorage` med lokal disk. App Service `/home` är beständig och delas mellan
      instanser.
    - Azure Blob-implementation i TODO.
12. **Mux Data i spelaren.** *Rek:* `disableCookies` och inget tittar-id skickas till Mux. Då
    behövs ingen cookie-banner, och appen sätter bara nödvändiga auth-cookies. Tekniska
    kvalitetsmått utan personkoppling behålls.
15. **SQLite-schema lokalt.**
    - *Rek:* migrationer bara för SQL Server, och SQLite skapas från modellen. Ett CI-test fångar
      glömda migrationer.
    - *Alternativ:* dubbla migrationsuppsättningar (SQL Server + SQLite) per modul. Det ger mer
      underhåll.
16. **Återbetalning i fas 1.** *Rek:* ja, hantera `charge.refunded` så att åtkomsten dras in och
    köpet räknas bort i avräkningen. Själva återbetalningen görs i Stripe Dashboard.
17. **Kundkonton per tenant** (samma e-post hos två arrangörer = två konton). *Rek:* ja, för
    tydlig isolering. Med Fidoco som säljare är Fidoco personuppgiftsansvarig för köpdata.
    Integritetspolicyn säger det, och arrangören ser bara aggregerade siffror (inga
    e-postadresser) i fas 1.
18. **Språk i README, ARCHITECTURE och TODO.**
    - *Rek:* engelska, i linje med koden.
    - PLAN.md är på svenska eftersom den är ett beslutsunderlag för dig.
19. **Vem bjuder in TenantAdmins?** *Rek:* PlatformAdmin bjuder in den första. En TenantAdmin
    kan sedan bjuda in fler i samma tenant. Bara en adminroll per tenant i fas 1.

---

## 14. Kända begränsningar och risker (dokumenteras i ARCHITECTURE.md)

- **En visning per köp upprätthålls av klienten för pågående uppspelning.**
  - En ny session får inga nya tokens för den gamla, och den gamla spelaren stoppas vid nästa
    heartbeat.
  - En manipulerad klient som ignorerar 409 kan i teorin spela vidare på en redan laddad stream.
  - Korta token-livstider begränsar spridning av länkar.
  - Hårdare skydd (DRM, Mux playback restrictions) ligger i TODO. Det är rimligt för en
    produkt till 199 kr.
- **Flera instanser kräver Redis** för `IDistributedCache` och `IViewerPresence`. Rate limiting
  är per instans, vilket är acceptabelt. Koden är förberedd, men fas 1 driftsätts med en
  instans.
- **Belastning, 2 000 samtidiga tittare:** cirka 67 heartbeats/s, utan DB, och en topp av
  sessionsstarter vid eventstart (en indexerad DB-läsning + lokal JWT-signering vardera). Det
  klaras med god marginal av en instans. Videon går via Mux CDN. Eventsidan och statusen
  output-cachas.
- **Swish via Stripe** måste vara tillgängligt och aktiverat för kontot. Det aktiveras i Stripe
  Dashboard och kräver ingen kod. Async-betalningar hanteras redan av flödet.
