# DocuCalendar — calendar.docurest.com

Appointment calendars for Docurest accounts, and the booking API the Docurest AI uses when a
caller or a chat visitor asks for a meeting.

One account has as many calendars as it has staff. Each calendar knows its working week, the time
its owner has marked busy, and the appointments already taken; from those three things it produces
the only times anyone may be offered. The AI collects a visitor's name and phone, offers real
slots, and books one — never inventing a time, never taking a time somebody already has.

## Shape

| Piece | What it is |
|---|---|
| `src/DocuCalendar.Domain` | Entities. No dependencies. |
| `src/DocuCalendar.Application` | `SlotEngine` — the pure function that decides what is offerable. |
| `src/DocuCalendar.Infrastructure` | EF Core, Postgres, booking, tenant + calendar resolution, webhooks. |
| `src/DocuCalendar.WebApi` | HTTP: booking API, provisioning, SSO, management endpoints. |
| `client/` | React + Vite SPA — the site staff use. |
| `tests/DocuCalendar.Tests` | The slot engine's specification. |

## How the two systems meet

```
Docurest                                   DocuCalendar
────────                                   ────────────
CalendarClient  ──── X-Master-Key ───────► POST /api/tenants/provision   → api key (once)
                ──── X-Api-Key ──────────► GET  /api/booking/slots
                ──── X-Api-Key ──────────► POST /api/booking/book
                ──── X-Api-Key ──────────► PUT  /api/contexts
"My Calendar" ── signed SSO token ───────► GET  /api/session/sso         → session cookie
POST /api/calendar/webhooks/booked ◄────── booking notifications (HMAC-signed)
```

There is no password on this site. The only way in is a short-lived token Docurest signs for a
user it has already authenticated; the only way to book is an API key Docurest was given once.

## Where the data lives

Every table is namespaced under a **`calendar` schema**, with its own EF migration history, and the
migration creates that schema itself.

It was designed to have a database of its own — that is still the right shape, because this service
would then hold credentials reaching nothing but calendars. In production it shares the host
application's database instead: creating a new one needs a Postgres superuser that could not be
produced, and a schema was the closest isolation available without one. The cost is recorded rather
than hidden: **the connection string this service uses also reaches the host application's data.**

Moving to a dedicated database later is a connection-string change plus a dump/restore of this one
schema. No code changes:

```bash
pg_dump -h 10.0.0.2 -U ragstudio -d ragdb2 -n calendar -f calendar.sql
# create caldb + docucalendaruser as a superuser, then:
psql -h 10.0.0.2 -U docucalendaruser -d caldb -f calendar.sql
```

## Running it locally

```bash
# API — needs a Postgres to talk to
dotnet run --project src/DocuCalendar.WebApi        # http://localhost:5014

# SPA — proxies /api to 5014
cd client && npm install && npm run dev             # http://localhost:5173
```

`appsettings.Development.json` carries development-only secrets and a local connection string.
Production values are never in this repository — see below.

## Deploying

`main` → GitHub Actions → the Hetzner box, blue/green with a health gate: the new slot must answer
`/api/health` before nginx is pointed at it. See `.github/workflows/deploy.yml`, and
`docs/SERVER-SETUP.md` for the one-time server preparation.

Secrets (`Docurest:MasterKey`, `Docurest:SsoSecret`, the connection string) live in
`/var/www/docucalendar/api-*/appsettings.Production.json` on the server. The deploy excludes that
file from rsync, so it is neither uploaded nor overwritten. The API refuses to start in production
if either secret is missing or shorter than 32 characters — a calendar service that boots with an
empty signing secret would accept forged identities from anyone.

## Tests

```bash
dotnet test
```

They are about the slot engine, because that is where a bug does not throw — it sends someone to
an office where nobody is expecting them.
