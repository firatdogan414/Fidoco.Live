# Driftrutin för galadagen

Checklista för en stor sändning (t.ex. Apex Fight Night: ~400 köpare, en kväll). Kommandona gäller
produktion; byt värdena nedan om dina namn skiljer sig. Bakgrund om infrastrukturen finns i
[README.md](README.md#deploy-to-azure-app-service--azure-sql).

```bash
RG=fidoco-production              # resursgrupp
APP=fidoco-prod                   # webbappen (AZURE_APP_NAME)
PLAN=$APP-plan
SQL=$APP-sql
DB=fidoco
HOST=https://admin.fidoco.se      # plattformsvärden (webhooks, /health)
SITE=https://afn.fidoco.se        # arrangörens sida
```

Grundregel: **ingen driftsättning till produktion från dagen före galan till dagen efter.** Godkänn inte
väntande produktionskörningar i GitHub. En körning kör Bicep, och Bicep återställer storlekarna till
`APP_SERVICE_SKU`/`SQL_DATABASE_SKU` och startar om appen.

---

## Före

### Två–tre dagar före

- [ ] **Provsändning** på ett separat testevent, inte på galans event. När signalen kommer går ett event
      automatiskt till *live*, och efter 5 minuter utan signal blir det *avslutat*.
  1. Admin → *Nytt event* "Provsändning", publicera, *Skapa sändning*.
  2. Ge dig själv en friplats (eventsidan → *Friplatser*). Det testar också mejl och inloggningslänk.
  3. Starta vMix/OBS med samma inställningar, uppkoppling och plats som på galan (README → *Connect vMix or OBS*).
     Kontrollera att status blir *Tar emot signal* och att eventet går live inom 15 s.
  4. Titta på mobil (4G) och dator. Kontrollera bild, ljud och fördröjning (~20–30 s med `standard`).
     Starta på en andra enhet: den första ska stoppas inom 30 s (*Din biljett används nu på en annan enhet*).
  5. Dra ur nätverkskabeln i 1–2 minuter. Spelaren ska vänta och fortsätta när signalen kommer tillbaka.
  6. Stoppa sändningen. Efter 5 minuter blir eventet *Avslutat*. Kontrollera att en inspelning dyker upp under
     *Inspelningar för repris* och att reprisen spelas.
- [ ] **Provköp i produktion** med ett riktigt kort eller en 100 %-kampanjkod
      (README → *Stripe*, punkt 6). Kvittot ska komma fram, inklusive till Gmail och Outlook (kolla skräpposten).
      Ett kortköp återbetalar du sedan i Stripe och kontrollerar att biljetten blir *Återbetald* under Support.
- [ ] **Friplatser** till press, sponsorer och personal delas ut i förväg (eventsidan → *Friplatser*).
- [ ] **Supportbemanning**: alla som ska svara köpare har ett administratörskonto (Admin → *Administratörer*) och
      har provat Support-sidan. Bestäm vilken kanal köparna når er på (arrangörens mejl och sociala medier).
- [ ] **Reservutrustning**: UPS för encoder, router och switch. 4G/5G-router som reserv, helst bondad
      uppkoppling. En andra encoder med samma strömnyckel. Strömnyckeln finns utskriven hos teknikern.

### Dagen före

- [ ] **Skala upp** (tar några minuter; appen startar om och databasen kopplar om under några sekunder, så gör det
      inte under pågående försäljningstopp):

  ```bash
  az appservice plan update -g $RG -n $PLAN --sku P1V3            # 2 vCPU / 8 GB (från B1)
  az sql db update -g $RG -s $SQL -n $DB --service-objective S3     # 100 DTU (från S0)
  az appservice plan show -g $RG -n $PLAN --query sku.name -o tsv   # kontroll: P1v3
  az sql db show -g $RG -s $SQL -n $DB --query currentServiceObjectiveName -o tsv   # kontroll: S3
  ```

  Sätt också variablerna `APP_SERVICE_SKU=P1v3` och `SQL_DATABASE_SKU=S3` i GitHub-miljön `production`. Då skalar
  en driftsättning inte ner av misstag. Går `P1V3` inte att välja för planen, använd `P0V3` eller `S1`.
  Kör alltid **en** instans: flera instanser kräver Redis (README → *Scaling out*).
- [ ] **Health**: alla checkar ska vara `Healthy`. Outbox-checken visar `pending`, `overdue` och `failed` per modul;
      `failed` och `overdue` ska vara 0.

  ```bash
  curl -s $HOST/health | jq '{status, checks: (.checks | map_values(.status))}'
  curl -s $HOST/health | jq '.checks.outbox'
  ```

- [ ] **Webhooks**
  - Stripe Dashboard → Developers → Webhooks → endpointen `$HOST/api/v1/webhooks/stripe`: status *Enabled*,
    händelserna `checkout.session.completed`, `checkout.session.async_payment_succeeded`,
    `checkout.session.async_payment_failed`, `checkout.session.expired` och `charge.refunded`. Senaste leveranserna
    ska ha svar 200; provköpet syns här.
  - Mux Dashboard → Settings → Webhooks → `$HOST/api/v1/webhooks/mux`: senaste leveranserna från
    provsändningen ska ha svar 200.
- [ ] **Eventet**: publicerat, rätt starttid, bild, pris och reprisdagar. *Skapa sändning* är gjord och strömnyckeln
      är inlagd i encodern. Kampanjkoderna är aktiva i Stripe (eller avstängda om de inte ska gälla).

### Galadagen, 3 timmar före

- [ ] `curl -s $HOST/health | jq .status` → `"Healthy"`.
- [ ] Öppna och logga in i flikarna för bevakningen nedan.
- [ ] **T−15 min**: starta encodern mot galans strömnyckel med en vinjett eller nedräkning i bild. Eventet går live
      och köparna ser vinjetten i stället för nedräkningen. Kontrollera *Tar emot signal* på eventsidan i admin.

---

## Under

### Vad jag bevakar

| Var | Vad | Agera när |
|---|---|---|
| Admin → eventet | Status *Tar emot signal*, *Tittar just nu*, sålda biljetter | Status *Ingen signal*, eller tittarantalet rasar |
| Mux Dashboard → Live Streams → strömmen | Inkommande bitrate, tappade bildrutor, *Active* | Bitraten sjunker eller tappade bildrutor ökar: kontakta teknikern (uppkoppling) |
| Application Insights → Live Metrics | Anrop/s, misslyckade anrop, svarstid, CPU, minne | Misslyckade anrop över ~1 %, svarstid över 1 s, CPU över 80 % i flera minuter |
| Azure Portal → SQL-databasen → Metrics | DTU-procent | Över 80 % i flera minuter: `az sql db update ... --service-objective S4` (kort omkoppling) |
| `curl -s $HOST/health \| jq .status` var 5:e minut | `Healthy` | `Unhealthy`: se *Plattformen svarar inte* nedan. `Degraded`: Stripe eller Mux svarar inte (se deras statussidor) |
| Stripe Dashboard → Payments och Webhooks | Betalningar kommer in, webhookleveranser 200 | Misslyckade leveranser: se *Har betalat men kommer inte in* |
| Admin → Support → *Senaste supportåtgärder* | Vad kollegorna gör | – |

Användbara frågor i Application Insights → Logs:

```kusto
// Fel de senaste 15 minuterna
union exceptions, traces
| where timestamp > ago(15m) and severityLevel >= 3
| summarize count() by tostring(customDimensions.CategoryName), outerMessage, message
// Uppspelning: startade sessioner och ersatta sessioner per minut
customMetrics
| where timestamp > ago(30m) and name in ("fidoco.playback_sessions_started", "fidoco.sessions_replaced")
| summarize sum(valueSum) by name, bin(timestamp, 1m)
```

### "Jag har betalat men kommer inte in"

Allt du gör under Support loggas automatiskt.

1. **Fråga efter e-postadressen** som köparen skrev vid betalningen. Sök under Admin → **Support**; det räcker med
   en del av adressen (minst 3 tecken), så stavfel hittas också.
2. **Köparen hittas och biljetten visar *Kan titta på livesändningen nu*:**
   - Klicka **Skicka länk till eventet**. Länken gäller i 24 timmar, kan användas en gång och leder direkt till
     sändningen. Be köparen leta i skräpposten och öppna länken på enheten där de ska titta.
   - Visar biljetten **Tittar nu** och köparen säger "det står att biljetten används på en annan enhet"? Biljetten
     gäller en enhet åt gången, och den senast startade vinner. Be dem stänga sändningen på den andra enheten,
     eller bara starta igen på den de vill använda.
   - Sidan säger att de inte har någon biljett? De är troligen inloggade med en annan adress. Be dem logga ut
     (*Logga ut*) och använda länken.
3. **Köparen hittas men biljetten säger något annat:**
   *Giltig. Sändningen har inte startat* betyder att vi inte är live än. *Återbetald* betyder att köpet är
   återbetalat i Stripe. *Indragen friplats* betyder att friplatsen är indragen.
4. **Köparen hittas inte:** sök på adressen eller beloppet i **Stripe Dashboard → Payments**.
   - **Betalningen är *Succeeded*** men köpet finns inte hos oss: bekräftelsen (webhooken) har inte kommit fram.
     Öppna betalningen → *Events and logs* → `checkout.session.completed` → **Resend**, eller Developers →
     Webhooks → endpointen → leveransen → *Resend*. Sök igen under Support efter några sekunder. Kvittot skickas
     automatiskt.
   - **Betalningen är *Incomplete/Pending*** (t.ex. Swish som inte bekräftats): köpet blir klart när Stripe skickar
     `async_payment_succeeded`. Behöver köparen komma in direkt: ge en friplats (nästa punkt), så slipper de betala igen.
   - **Betalt med en felstavad adress** som köparen inte kommer åt: ge en **friplats** till rätt adress med
     anteckningen *"Betalt i Stripe som <fel adress>, order …"*. Köpet finns kvar i avräkningen; friplatsen räknas
     som 0 kr, så inget räknas dubbelt.
   - **Ingen betalning finns**: köparen har inte betalat (avbrutet köp eller nekat kort). Hänvisa till eventsidan.
5. **Inga mejl kommer fram till någon** (inloggningslänkar och kvitton saknas): `curl -s $HOST/health | jq .checks.outbox`.
   Ökar `pending`, `overdue` eller `failed`, kontrollera e-postleverantören (statussida, SMTP-lösenordet i Key Vault) och
   loggarna (*Sending … failed*). Mejlen skickas om automatiskt när leverantören fungerar igen (upp till 10 försök,
   som längst var 10:e minut).

### Om strömmen bryts

**Signalen från arenan försvinner** (status *Ingen signal*, spelarna buffrar):

- Mux väntar **5 minuter** (reconnect window) innan sändningen räknas som avslutad. Kommer signalen tillbaka inom
  den tiden fortsätter allt av sig självt.
- Teknikern: starta om strömningen i vMix/OBS, eller byt till reservuppkopplingen (4G/5G) eller reservencodern med
  samma strömnyckel.
- Är signalen borta **längre än 5 minuter** blir eventet *Avslutat*, och tittarna ser att sändningen är slut, eller
  reprisen om den finns. **Eventet går live igen automatiskt** när signalen kommer tillbaka, och tittarnas sidor
  byter tillbaka inom 15 sekunder. Säg till köparna att ladda om sidan.
- Varje avbrott över 5 minuter ger en ny inspelning. Efter galan väljer du vilken inspelning som är reprisen
  (eventsidan → *Inspelningar för repris* → *Använd för repris*).
- Strömavbrott på arenan: encoder och router ska gå på UPS. Räcker inte UPS:en, byt till reservencodern på en
  annan elkrets eller till 4G.
- Informera köparna i arrangörens kanaler (sociala medier, mejl), t.ex.: *"Vi har tekniska problem med
  sändningen från arenan och är tillbaka inom kort. Din biljett gäller – ladda om sidan när vi är tillbaka."*

**Plattformen svarar inte** (`/health` svarar inte eller är `Unhealthy`, sidorna laddar inte):

- Den som redan tittar påverkas inte direkt. Videon kommer från Mux CDN, uppspelningsnyckeln gäller i 15 minuter
  och spelaren försöker igen utan att avbryta bilden. Nya tittare och köp fungerar inte.
- Felsök i den här ordningen:

  ```bash
  curl -s $HOST/health | jq '.checks | map_values(.status)'      # vilken check fallerar?
  az webapp log tail -g $RG -n $APP                              # loggarna live
  az webapp restart -g $RG -n $APP                               # omstart (~1 min)
  az sql db show -g $RG -s $SQL -n $DB --query status -o tsv     # ska vara Online
  ```

- Azure, Mux och Stripe har statussidor: status.azure.com, status.mux.com och status.stripe.com. Vid ett avbrott hos
  dem: informera köparna och vänta.

**Stripe nere** (`stripe` är `Degraded`, köp misslyckas): de som redan har biljett påverkas inte. Informera dem
som vill köpa. Webhooks som inte kommit fram skickar Stripe om automatiskt i upp till tre dygn.

---

## Efter

### Samma kväll

- [ ] Stoppa encodern när sändningen är slut. Eventet avslutas automatiskt efter 5 minuter. Det går också med
      **Avsluta eventet** på eventsidan, men gör det först när signalen är stoppad: kommer signalen tillbaka går
      eventet live igen.
- [ ] Kontrollera att inspelningen är klar och vald för repris (eventsidan → *Inspelningar för repris*).
      Provspela reprisen med din friplats.

### Dagen efter

- [ ] **Skala ner** (köparna som tittar på repris klarar sig på B1/S0):

  ```bash
  az appservice plan update -g $RG -n $PLAN --sku B1
  az sql db update -g $RG -s $SQL -n $DB --service-objective S0
  ```

  Ta bort, eller sätt tillbaka, `APP_SERVICE_SKU`/`SQL_DATABASE_SKU` i GitHub-miljön `production`. Driftstoppet för
  produktion upphör.
- [ ] Gå igenom **Application Insights** (fel under kvällen), `/health` (outbox `failed` = 0) och
      **Senaste supportåtgärder**. Notera vad som kan göras bättre till nästa gala.

### Återbetalningar

- Görs i **Stripe Dashboard → Payments → betalningen → Refund**, och alltid som **hel** återbetalning. Webhooken
  `charge.refunded` drar då in biljetten (en pågående uppspelning stoppas inom 30 s), och köpet räknas inte i
  försäljning eller avräkning. Kontrollera under Support att biljetten visar *Återbetald*.
- **Delåterbetalningar** (t.ex. 50 kr som kompensation) syns inte i Fidoco. Köpet räknas fullt i avräkningen, så de
  måste regleras manuellt mot arrangören. Undvik dem.
- **Tvister/chargebacks** hanteras i Stripe. Fidoco drar inte in biljetten automatiskt (se TODO.md). Är det uppenbart
  missbruk kan du återbetala, vilket drar in biljetten.
- Friplatser som inte ska gälla längre dras in på eventsidan (*Friplatser* → *Dra in*).

### Avräkning

1. Vänta tills återbetalningarna är klara, normalt när repristiden har gått ut. Avräkningen räknas om löpande, så
   en återbetalning efteråt ändrar den.
2. Eventsidan i admin → **Avräkning** → **Ladda ner CSV**. Beloppen är vad köparna faktiskt betalade (efter
   kampanjkoder). Återbetalda köp räknas inte, och friplatser står med som 0 kr. Plattformsadmin → *Avräkning*
   visar alla arrangörer för en period.
3. Stäm av mot Stripe: summan *Betalt inkl moms* för genomförda köp ska vara lika med Stripes bruttobelopp för
   eventets betalningar, minus hela återbetalningar (Payments, filtrera på datum). Stripes avgifter är Fidocos kostnad
   och påverkar inte avräkningen.
4. Arrangören fakturerar Fidoco *Att fakturera Fidoco* (andel exkl. moms plus 25 % moms om arrangören är
   momsregistrerad) med CSV:n som underlag. Fidoco redovisar momsen på försäljningen.
