# White-label calendar — `https://<portal-host>/calendar/`

_Written 2026-09-14 for implementation by a separate session. Decisions below were made with the
owner; do not re-open them. Read `EXTERNAL-CALENDAR-SYNC-PLAN.md` first for how the calendar,
its embedding in Docurest and the OAuth round trip work today._

## The problem this solves

Docurest is white-labelled: `https://ai-assistant.wiut.uz` serves the same app as
`docurest.com`, brand-neutral, and the same users work on both. The calendar lives on
`calendar.docurest.com` and Docurest's **My Calendar** page frames it
(`myrag.client/src/pages/CalendarLaunchPage.tsx` → iframe whose `src` is the SSO link).

On docurest.com that works because docurest.com and calendar.docurest.com are the **same site**
(one registrable domain), so the calendar's session cookie (`docucalendar.session`,
`SameSite=Lax`) is sent inside the frame. On **ai-assistant.wiut.uz it does not work**: wiut.uz
framing docurest.com is cross-site, browsers withhold the cookie (Safari and Firefox always,
Chrome increasingly), and the frame shows "Your calendar session has ended". Nobody has hit this
yet only because the WIUT portal's My Calendar has not been opened since the embed shipped.

Making the cookie `SameSite=None` would not be a fix — third-party cookies in frames are being
removed by the browsers regardless of the attribute.

## Decisions (settled)

| Question | Decision | Why |
|---|---|---|
| Where does the calendar live for a white label? | **Under a path on the portal's own host**: `https://ai-assistant.wiut.uz/calendar/` | Same origin as the app → the cookie and the frame just work; no DNS, no certificate, nothing from the customer's IT. A subdomain per customer (`calendar.wiut.uz`) is the opposite of "easy and fast". |
| Does `calendar.docurest.com` stay? | **Yes, canonical.** It moves to the same shape: the UI at `/calendar/`, `/` redirects there. | One build, one path, one router base on every host. The machine-to-machine API (`/api/booking`, `/api/tenants/provision`, `/api/contexts`, `/api/people`) stays at `/api/` on calendar.docurest.com — Docurest's `CalendarClient` keeps working unchanged. |
| Where do Google/Microsoft send the browser back? | **Always `calendar.docurest.com`** (`Sync:PublicBaseUrl`), as today. | Redirect URIs are registered per host in both consoles; one canonical callback host means zero console work per white label. The existing `returnTo` mechanism (`SyncController.Landing`) brings the browser back to the portal afterwards. Users see docurest.com for the two seconds of the bounce; accepted. |
| Who may embed / be returned to? | The portal hosts Docurest already knows (`PublicApi:PortalHosts`), pushed to the calendar. | One list, owned by Docurest, feeding both `Sync:EmbedHosts` and the frame policy. |
| Branding on the calendar's own pages? | From the host: the portal brand Docurest already injects (`window.__PORTAL_BRAND__`), or "DocuCalendar" on docurest hosts. | Embedded mode hides the brand anyway; only the "own tab" view shows it. |
| Self-service white labels from the admin dashboard? | **Phase 4**, designed here, built after the calendar path change. | Certificates are the real blocker; the rest is a table and a page. |

## How it fits together after the change

```
https://ai-assistant.wiut.uz/app/calendar        Docurest SPA page (unchanged component)
   └── <iframe src="https://ai-assistant.wiut.uz/calendar/api/session/sso?t=…&next=…">
         nginx: location /calendar/api/  → docucalendar_backend (/api/…), Location headers
                                            rewritten back under /calendar/
         nginx: location /calendar/      → the calendar SPA (built with base /calendar/)
         cookie docucalendar.session set on ai-assistant.wiut.uz — same origin as the frame
```

Same two nginx locations on `docurest.com` and on `calendar.docurest.com`. The calendar API
itself does not know or care which host it is behind, with one exception: the SSO link and the
OAuth landing must name the **portal** host, which Docurest knows from the request it is
answering.

## Phase 1 — the calendar under `/calendar/` on every host

### Calendar client (`docucalendar/client`)

1. `vite.config.ts`: `base: '/calendar/'`. Assets are then emitted as `/calendar/assets/…`, which
   is what makes one build serve every host. (Do **not** use `base: './'`: relative asset URLs
   break on deep links such as `/calendar/schedule/<id>`.)
2. `src/main.tsx`: `<BrowserRouter basename="/calendar">`.
3. `src/api.ts`: every call goes to `` `/calendar/api${path}` `` instead of `` `/api${path}` ``;
   `connectUrl()` returns `/calendar/api/sync/…`. Introduce one constant `API_BASE = '/calendar/api'`
   and use it everywhere (grep for `'/api` in `client/src` — `RoutingPage.tsx` has a raw
   `fetch('/api/contexts')`).
4. `src/App.tsx`: the "Own tab" link (`href="/"`) becomes `href="/calendar/"`; the SignedOut card's
   "Go to Docurest" link becomes host-relative: `href="/app/calendar"` (it is on the same host now).
5. Branding (`App.tsx` header + `<title>`): read `window.__PORTAL_BRAND__` if nginx injected it
   (see nginx below) and fall back to "DocuCalendar". Keep `PortalGuard.tsx`-style tolerance for
   the literal placeholder token. The Guide page mentions "Docurest" by name in a few places;
   replace with the brand where it means the portal, leave where it means the product.

### Calendar API (`docucalendar/src`)

6. **Redirects**: `SessionController.Sso` returns `Redirect("/")` / `Redirect(next)`, and
   `SyncController` returns `Redirect("/calendars?…")`. Do not touch them — nginx rewrites the
   `Location` header (`proxy_redirect / /calendar/;`). The `next` allow-rule in `Sso` (local path,
   no `//`) stays; `next` values are written by our own code (`/calendars?connected=google`) and
   come out as `/calendar/calendars?…` after the nginx rewrite.
7. **Return address after OAuth** (`SyncController.Landing`): today
   `` `{origin}/app/calendar?next=…` `` — correct as is: `origin` is the portal host, the Docurest
   page there re-embeds the calendar with `next`.
8. **Embed hosts** (`SyncOptions.EmbedHosts`): replace the config list with a table
   `EmbedHosts(TenantId?, Host)` fed by Docurest — add `PUT /api/embed-hosts` under
   `[RequireApiKey]` (same pattern as `ContextsController` / `PeopleController`), body
   `{ hosts: ["ai-assistant.wiut.uz"] }`; `AllowedReturn()` checks config **or** table. Keep the
   config entries as a fallback so nothing regresses before Docurest pushes.
9. **CORS** (`Program.cs`): unnecessary once the UI is same-origin on every host; leave the policy
   but it no longer needs portal hosts.
10. `SessionController.Me`: return `brand` too if the API can see the injected brand — it cannot
    (nginx injects into HTML only), so branding stays a client concern. Nothing to do here.

### Docurest side (`MyRag`)

11. `CalendarController.SsoLink` (`src/RAGStudio.WebApi/Controllers/CalendarController.cs`):
    build the link **for the host the request came from**. Read the request host (`Request.Host`,
    honouring `X-Forwarded-Host` — nginx sets `Host $host`, which is enough). If it is one of
    `PublicApi:PortalHosts` (already bound in `PublicApiOptions`) or the main app host, the base
    becomes `` `https://{host}/calendar` ``; otherwise `Calendar:BaseUrl` + `/calendar`. Pass it
    to `CalendarClient.BuildSsoUrl` as a new optional `baseOverride`. The SSO **token** is
    unchanged (same secret, same payload).
12. `CalendarController.SsoLink` also pushes the portal host list to the calendar
    (`CalendarClient.PushEmbedHostsAsync` → `PUT /api/embed-hosts`), fire-and-forget like
    contexts and people.
13. `CalendarLaunchPage.tsx`: no change — it frames whatever URL the API returns, and the
    "Open in its own tab" link derives the origin from that URL, which is now the portal host.
    Verify `new URL(url).origin + '/calendar/'` is used for that link (today it is `origin` only;
    make it `origin + '/calendar/'`).

### nginx (server; `/etc/nginx/sites-available/*`)

14. On **every** host that serves the app (`docurest.com`, `ai-assistant.wiut.uz`, future
    portals) and on `calendar.docurest.com`, add:

    ```nginx
    # The calendar, under its own path. Same origin as the app, so its cookie works in the frame.
    location /calendar/api/ {
        rewrite ^/calendar(/api/.*)$ $1 break;
        proxy_pass http://docucalendar_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Prefix /calendar;
        # The backend redirects to "/", "/calendars?…": put them back under the prefix.
        proxy_redirect / /calendar/;
    }
    location /calendar/ {
        alias /var/www/docucalendar/client/;
        index index.html;
        try_files $uri $uri/ /calendar/index.html;
        add_header Content-Security-Policy "frame-ancestors 'self'" always;
        # Brand injection, same mechanism the portal uses for app.html (sub_filter on the
        # __PORTAL_BRAND__ placeholder); on docurest hosts leave the placeholder — the client
        # treats the literal token as "unset".
    }
    ```
    On `calendar.docurest.com` additionally `location = / { return 302 /calendar/; }` and keep
    the existing `location /api/` (machine-to-machine, no `proxy_redirect`). Remove the old
    `location /` SPA block there once `/calendar/` serves.

    `frame-ancestors 'self'` is now enough everywhere: the frame and the page are the same origin.

15. The calendar's client `index.html` needs the brand placeholder script the portal shell has
    (`<script>window.__PORTAL_BRAND__="__PORTAL_BRAND__";</script>`) so `sub_filter` has something
    to replace — look at how `app.html` does it on the portal block (`sub_filter` directives in
    `/etc/nginx/sites-available/ai-assistant.wiut.uz`) and mirror it.

16. The calendar deploy workflow (`.github/workflows/deploy.yml`) rsyncs `client/dist` to
    `client-<slot>` and flips a symlink; unchanged. The `.active-env` gate and the
    `docucalendar_backend` upstream are unchanged.

### Prove it (Phase 1)

1. `https://calendar.docurest.com/` → 302 → `/calendar/` → the app loads, signed out card shows.
2. Docurest → My Calendar on **docurest.com**: frame works as before (URL inside the frame is
   `docurest.com/calendar/…` — check in devtools).
3. Docurest → My Calendar on **ai-assistant.wiut.uz** as the same user: the frame loads signed in
   (the bug this plan exists for); the "Own tab" link opens `https://ai-assistant.wiut.uz/calendar/`
   with the WIUT brand, not "DocuCalendar".
4. On the portal, Connect Google on a calendar: browser goes to Google, comes back via
   `calendar.docurest.com/api/sync/google/callback`, lands on
   `https://ai-assistant.wiut.uz/app/calendar?next=/calendars?connected=google`, the frame shows
   the green "connected" notice.
5. The phone still books (`/api/booking/*` on calendar.docurest.com untouched) — one test call.
6. `curl -sI https://ai-assistant.wiut.uz/calendar/assets/<hash>.js` → 200; a deep link
   `https://ai-assistant.wiut.uz/calendar/schedule/<id>` refreshes without 404.

### Traps

- The rewrite must keep the query string: nginx `rewrite … break` does by default; do not add a
  trailing `?`.
- `proxy_redirect / /calendar/;` matches Location headers that start with `/`. The backend never
  emits absolute `http://` URLs for local redirects; keep it that way (`Redirect("/…")`).
- `try_files … /calendar/index.html` with `alias`: the fallback must be the **URI**, not the file
  path. Test a deep link.
- Vite emits `<base>`-less HTML; with `base: '/calendar/'` the script tags are absolute
  `/calendar/assets/…`. Do not add `<base href>`.
- `X-Forwarded-Prefix` is set for completeness; the API does not need it in Phase 1. If a future
  change makes the API build absolute local URLs, use it there.
- Docurest's `appsettings.Production.json` is committed and rsynced (see the memory note on prod
  config); `PublicApi:PortalHosts` lives there, so adding a portal host today is a commit.

## Phase 2 — branding from Docurest rather than nginx (small)

Nginx `sub_filter` is the portal's current branding mechanism and works, but it means a nginx
edit per host. Better: the calendar asks Docurest. Add `GET /api/portal/branding?host=…` to
Docurest (anonymous, cached, answers `{ brand, initials }` from the same source the shell uses —
in Phase 4 the `WhiteLabel` table). The calendar client calls it once on load with
`location.host` and falls back to "DocuCalendar". Then item 15 above can go.

## Phase 3 — retire the hard-coded lists

`Sync:EmbedHosts` (calendar) and the nginx `frame-ancestors` lists are gone by the end of
Phase 1 (`'self'`). `PublicApi:PortalHosts` (Docurest) remains until Phase 4 replaces it with the
table. Nothing else references portal hosts by name — verify with `grep -rn "wiut" src/` in both
repos before closing the phase.

## Phase 4 — self-service white labels from the global admin dashboard (the goal)

**What "done" looks like for the owner.** A new university, say TGTU, wants
`https://ai-assistant.tgtu.com` with its calendar at `https://ai-assistant.tgtu.com/calendar`:

1. Global admin → **White labels → Add**: host `ai-assistant.tgtu.com`, the account it serves,
   brand "TGTU AI Assistant", initials "TG", accent colour, optional logo. Save.
2. The page shows the one thing to send the university's IT:
   *"Create a DNS **A record** for `ai-assistant.tgtu.com` pointing at `77.42.95.87`"*
   (a CNAME to `portal.docurest.com` is accepted too — same result).
3. The row's checklist fills in by itself as things happen: **DNS** ✓ (resolves to us) →
   **Certificate** ✓ (issued within ~2 minutes of DNS) → **App** ✓ (`/login` answers, branded) →
   **Calendar** ✓ (`/calendar/api/health` answers) → **Widget** ✓ (host accepted as first-party).
   A **Check now** button re-runs the probes.
4. Nobody touches the server. Nobody asks Claude. Disabling the row switches the host off.

Everything a portal host needs today that is hand-made — the nginx server block, the certificate,
the `PublicApi:PortalHosts` entry, the brand injection, the calendar's embed-host entry — is
replaced by **one catch-all nginx block, one table, and one certificate job**.

### Data (Docurest)

`WhiteLabel` entity in `RAGStudio.Domain.Entities`, migration in `Data/Migrations`:

| Column | Purpose |
|---|---|
| `Host` (unique, lower-case, no scheme) | `ai-assistant.tgtu.com` |
| `TenantId` | the account it serves; portal users must belong to it (`PortalGuard` today only hides marketing pages — add the tenant check at login) |
| `Brand`, `Initials`, `AccentColor`, `LogoUrl?` | what the shell and the calendar show |
| `Enabled` | off = brand endpoint 404s, widget auth refuses, calendar refuses as return host, certificate job stops renewing |
| `DnsOk`, `CertificateOk`, `AppOk`, `CalendarOk`, `LastCheckedAt`, `LastCheckError` | the checklist |
| `CreatedAt`, `CreatedByUserId` | audit |

Consumers that read the table (cached 5 minutes) instead of config:

- `WidgetAuthService._portalHosts` (drop `PublicApi:PortalHosts`; keep reading the config key as
  a seed for one release so nothing regresses).
- `CalendarController.SsoLink` host allow-list and the embed-host push to the calendar.
- New `GET /api/portal/branding?host=…` (anonymous, cached): `{ brand, initials, accentColor,
  logoUrl }` or 404. The app shell and the calendar client call it on load.
- New `GET /api/portal/hosts` for the certificate job (below), protected by a server-local token
  (`Portal:JobToken`, systemd drop-in env var — never in appsettings).

### Admin UI (Docurest, global admin only)

`/app/admin/white-labels` (route + `AppRoutes`, `Sidebar` under Admin, `NotOperator` +
global-admin policy on the API):

- list: host, brand, tenant, enabled, checklist chips (grey pending / green ok / red failed with
  the error on hover), last checked;
- add/edit drawer: host (validated: hostname only, lower-cased, no `docurest.com` hosts), tenant
  picker, brand, initials, colour, logo URL; the DNS instruction with a copy button appears after
  save;
- **Check now** → `POST /api/admin/white-labels/{id}/check` runs the probes inline and returns
  the row;
- **Disable / Enable**, **Delete** (only when disabled).

### Probes (`WhiteLabelProbeService`, Docurest)

Run by *Check now* and by a Hangfire job every 10 minutes for rows with any chip not green
(`[Queue("ragstudio")]`, self-removing, same pattern as `IntegrationMonitorJob`):

1. **DNS**: `Dns.GetHostAddressesAsync(host)` contains the server's public IP
   (`Portal:PublicIp`, config) — or the CNAME chain ends at `portal.docurest.com`.
2. **Certificate**: HTTPS `GET https://{host}/api/health` succeeds with a valid chain (a normal
   `HttpClient`; a TLS failure is the "not issued yet" signal).
3. **App**: `GET https://{host}/login` is 200 and the body contains the neutral shell marker.
4. **Calendar**: `GET https://{host}/calendar/api/health` returns `{"app":"docucalendar"}`.
5. **Widget**: `WidgetAuthService` accepts the host (in-process check).

### nginx: one catch-all block for every white label

Replace the per-host portal blocks (`/etc/nginx/sites-available/ai-assistant.wiut.uz` today) with
**one** block. The trick that makes it possible is that nginx (≥ 1.15.9; the server runs 1.24)
accepts **variables in `ssl_certificate`**, so one block serves any host whose certificate exists
on disk:

```nginx
server {
    listen 443 ssl http2 default_server;
    server_name _;                                     # every host not claimed by another block
    ssl_certificate     /etc/letsencrypt/live/$ssl_server_name/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$ssl_server_name/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    # … then exactly the location set of today's ai-assistant.wiut.uz block (app shell, /api/,
    # /hubs/, /widget/, /lib/, the /app → app.html fallbacks), plus the two /calendar/ locations
    # from Phase 1. No sub_filter: branding comes from /api/portal/branding by host.
}
server {
    listen 80 default_server;
    server_name _;
    location /.well-known/acme-challenge/ { root /var/www/html; }   # certificate issuance
    location / { return 301 https://$host$request_uri; }
}
```

A host with no certificate yet fails the TLS handshake (browser error) until the job below has
run — that is what the admin checklist shows in the meantime. `docurest.com` and
`calendar.docurest.com` keep their own explicit blocks; they are not portals.

Migration of WIUT: once the catch-all is in place, delete the hand-made
`ai-assistant.wiut.uz` block, add the row in the admin page, run *Check now*. Its certificate
files already exist under `/etc/letsencrypt/live/ai-assistant.wiut.uz/` (if it is on
`/etc/nginx/ssl/default.crt` today, the job issues a real one).

### The certificate job (server, no code in the repos beyond one script)

`/usr/local/bin/portal-certs.sh` on a systemd **timer every 2 minutes**:

```sh
hosts=$(curl -sf -H "Authorization: Bearer $PORTAL_JOB_TOKEN" https://docurest.com/api/portal/hosts)
for h in $hosts; do
  [ -d /etc/letsencrypt/live/$h ] && continue
  # Only when DNS already points here — otherwise Let's Encrypt fails and rate-limits us.
  dig +short "$h" | grep -q "^77\.42\.95\.87$" || continue
  certbot certonly --webroot -w /var/www/html -d "$h" --non-interactive --agree-tos \
      -m hcoder@gmail.com --deploy-hook "systemctl reload nginx" && touched=1
done
[ -n "$touched" ] && systemctl reload nginx
```

Renewal is certbot's own timer, already installed. Disabled rows are not returned by
`/api/portal/hosts`, so nothing new is issued for them; existing certificates simply lapse.
Failures land in the journal and, via the probe, on the admin page ("Certificate: not issued —
DNS does not point here yet").

**Alternative considered — Caddy with on-demand TLS.** Caddy would issue certificates on the
first visit with an `ask` endpoint against the table and make the job above unnecessary, at the
cost of putting a second edge server on port 443 and moving the docurest.com hosts behind it.
Rejected for now: the nginx design above reuses everything already running and needs no
cut-over; revisit if the timer's two-minute lag ever matters.

### Prove it (Phase 4)

1. Add `demo-portal.docurest.com` (an A record you control) in the admin page; within ~2 minutes
   the checklist goes green on its own; open it: branded shell, login works for a user of that
   tenant, `/calendar/` works, the widget accepts the host.
2. Delete the hand-made WIUT nginx block, add the WIUT row, *Check now* → all green; WIUT users
   notice nothing.
3. Disable the demo row: the brand endpoint 404s, the calendar refuses it as a return host, the
   next certificate run skips it.
4. `grep -rn "wiut" src/` in both repos finds nothing but tests and docs.

## What implementation changed (2026-09-14)

Built in one pass, Phases 1–4. Four things the plan had wrong or did not know:

1. **The API emits the `/calendar` prefix itself** (`UiPaths.Ui`), rather than relying on
   `proxy_redirect` as item 6 said. The plan missed that a provider's OAuth callback always lands
   on `calendar.docurest.com`'s *machine-to-machine* `/api/` location, which rewrites nothing — a
   redirect that was correct through the portal's `/calendar/api/` door escaped the prefix through
   that one. The prefix is now in the Location header whichever door the request came through, and
   **no `proxy_redirect` is configured anywhere** (it would double the prefix).
2. **`sites-enabled/docurest.com` is a real file, not a symlink**, so editing `sites-available`
   changed nothing there. Both copies are now written. And a backup written *into* `sites-enabled`
   is loaded by nginx as a duplicate server block — one shadowed the edited docurest.com block for
   several minutes. Backups now live in `/root/nginx-backups/`.
3. **A customer may front their own host.** `ai-assistant.wiut.uz` resolves to 195.158.11.214,
   WIUT's own proxy, which forwards to this server over **port 80** — so the calendar include had
   to go in the port-80 block too, and "does DNS point at us" is the wrong question. The probe now
   asks whether the host *serves our application over HTTPS*; DNS is consulted only to explain a
   failure. Both topologies (A record, or the customer's proxy) turn the row green.
4. **Per-host generated blocks instead of one catch-all.** `portal-sync.sh` renders
   `sites-enabled/portal-<host>.conf` from `snippets/portal-app.conf` and obtains a certificate
   when (and only when) DNS points here, on a two-minute systemd timer. This avoids taking over
   `default_server` on a live box, and it is what makes the customer-proxy case work: those hosts
   get a port-80 block and no certificate, which is exactly right.

**Server files added:** `/etc/nginx/snippets/docucalendar-path.conf` (the two `/calendar/`
locations, included by docurest.com, calendar.docurest.com and every portal),
`/etc/nginx/snippets/portal-app.conf` (the whole app shell, host-agnostic),
`/usr/local/bin/portal-sync.sh` + `portal-sync.{service,timer}`, `/etc/portal-sync.env`, and the
`ragstudio-api.service.d/portal.conf` drop-in holding `Portal__PublicIp` and the shared
`Portal__JobToken`.

## Out of scope

Custom domains for the **public** site (marketing pages are docurest-only), per-portal email
sender identities, and per-portal OAuth app registrations (customers who insist on their own
Azure app are the "bring your own keys" case noted in the sync plan).
