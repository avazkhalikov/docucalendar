# External calendar sync — Microsoft 365 and Google Calendar

_Phase 8 of the calendar platform. Written 2026-09-10; the decisions below were confirmed by the
owner before a line of code was written._

## What it does, in one paragraph

Any member of staff — the account owner or an operator — opens **Calendars**, picks the provider
their real calendar lives in (Outlook 365 or Google), signs in there once, and from then on the two
calendars keep each other honest every five minutes: whatever is busy in Outlook or Google becomes
busy here, so the assistant never offers a time the person has already given away; and every
appointment the assistant (or a colleague) books here appears in their Outlook or Google calendar
within seconds, with the visitor's name, number and topic. If they move or delete one of those
appointments from inside Outlook or Google, the appointment here follows, and the operator group is
told.

## Decisions (settled with the owner — do not re-ask)

| Question | Decision | Consequence in the design |
|---|---|---|
| Which OAuth apps? | **Reuse** what Docurest already has where it exists | Google: the existing Sign-In client (`872977…`) gains one redirect URI and the Calendar scope. Microsoft: **nothing exists** — the Email KB's Microsoft entries are empty in production and no Azure registration is recorded anywhere — so one app registration is created (checklist below). |
| Who sees mirrored event titles? | **Only the calendar's own owner** | `BusyBlock.Reason` holds the remote subject; the API blanks it unless the viewer is the person whose calendar it is. Everyone else sees "Busy". The account owner viewing an operator's calendar sees "Busy" too — a personal Gmail must not leak. |
| Appointment moved/deleted inside Outlook/Google? | **Follow the human** | Deleted there → cancelled here; moved there → moved here. Docurest is notified (`moved` / `cancelled` webhook events) so the operator group hears about it. |
| Cadence | Every **5 minutes**, plus an immediate push right after a booking | A `BackgroundService` with a 5-minute timer and a nudge channel. No Hangfire; this service has no queue and a timer is all it needs. |
| Which remote calendar? | The account's **primary** calendar | One connection per staff calendar. A calendar picker is a later nicety; nobody asked for it. |

## The data

New table `calendar."ExternalConnections"` — one row per connected staff calendar:

| Column | Purpose |
|---|---|
| `CalendarId` (unique) | the staff calendar this feeds |
| `Provider` | `microsoft` \| `google` |
| `AccountEmail` | which mailbox was connected — shown in the UI so a wrong-account connection is obvious |
| `RefreshTokenProtected`, `AccessTokenProtected`, `AccessTokenExpiresAt` | tokens, **encrypted with ASP.NET Data Protection** — never stored plain |
| `Status` | `connected` \| `reconnect` (refresh token dead) \| `error` (last run failed, will retry) |
| `LastSyncAt`, `LastSyncError`, `LastPulled`, `LastPushed` | what the UI shows, and what a support conversation starts from |
| `ConnectedByUserId`, `CreatedAt` | audit |

Columns added to existing tables:

- `Appointments`: `ExternalProvider`, `ExternalEventId`, `ExternalSyncedAt` — the remote copy of an
  appointment booked here. Null until pushed; cleared when the remote copy is deleted.
- `BusyBlocks`: nothing. `Source` (`microsoft` / `google`) and `ExternalId` were reserved for
  exactly this in Phase 1, and the `(CalendarId, Source, ExternalId)` index already exists. `Reason`
  carries the remote subject. Note: the reserved value was `"outlook"`; the sync writes the provider
  key `"microsoft"` so the two columns say the same thing everywhere.

## The sync, step by step (one connection, one run)

1. **Refresh the access token** if it expires within two minutes. `invalid_grant` means the person
   revoked access or the refresh token aged out → `Status = reconnect`, stop, surface in the UI. Any
   other failure → `Status = error`, retried next tick.
2. **Pull** every remote event in the window `[now − 1 day, now + HorizonDays + 1 day]`
   (Graph `calendarView`, Google `events.list` with `singleEvents=true` so recurrences arrive
   expanded). All pages, or nothing — a half-fetched window must never reach step 3, because step 3
   deletes what it did not see.
3. **Plan** — a pure function (`SyncPlanner`, fully unit-tested) turns *(remote events, existing
   mirrored blocks, appointments with a remote id)* into a list of changes:
   - a remote event that is **one of ours** (its id matches an appointment's `ExternalEventId`):
     time changed → *move the appointment*; cancelled/deleted → *cancel the appointment*; otherwise
     nothing. It is **never** mirrored as a busy block — that would double-count it.
   - any other remote event that occupies time (not marked free/transparent, not cancelled) →
     *upsert a busy block* keyed by `(CalendarId, Source, ExternalId)`; all-day events block the
     whole local day.
   - a mirrored block inside the window whose remote event is gone → *remove it*.
   - an appointment of ours inside the window whose remote copy is gone → *cancel it* (the human
     deleted it there).
4. **Apply** the plan in one transaction. Moved/cancelled appointments raise Docurest webhooks
   (`appointment.moved`, `appointment.cancelled`) after commit.
5. **Push**: every confirmed appointment on this calendar with no `ExternalEventId` and a start in
   the future → create the remote event (subject `Appointment: {visitor}`, body with phone, topic,
   channel and "booked via DocuCalendar"), store its id. Cancelled here with a remote id → delete the
   remote copy, clear the id.

Echo safety: our pushed events come back in the next pull as *ours* (step 3, first bullet) and are
skipped; mirrored blocks are never pushed anywhere. Nothing loops. "Ours" means **every remote id
this service ever minted on the calendar, whatever the appointment's status** — a cancelled
appointment keeps its id until step 5 deletes the copy, and the pull in step 2 runs first. The
first live cancellation proved why: for one run the still-present copy was mirrored as a phantom
busy block on exactly the slot the cancellation had freed.

## Proven live (2026-09-14, Google)

Connected hcoder@gmail.com to the owner's calendar. Booked here → in Google within a second of
the nudge; an event created in Google ("test") → busy here on the next 5-minute tick, title
visible to the owner; cancelled here → deleted from Google. Two defects surfaced and fixed on
the day: `calendars.get` is outside the `calendar.events` scope (the account email is read from
the events listing's `summary` instead), and the phantom block above.

## Inside Docurest

Docurest's **My Calendar** page frames this site (same site → the session cookie works in the
frame). Framed, the app hides its brand/account/sign-out and keeps the navigation. Provider
sign-ins cannot render in a frame, so the connect links take the whole tab and the callback
returns to `{embedHost}/app/calendar?next=/calendars?connected=…`; Docurest passes `next` into
the SSO link and the sign-in door lands there (local paths only). Return hosts are an allow-list
(`Sync:EmbedHosts`), and nginx serves `Content-Security-Policy: frame-ancestors` naming the same
hosts.

The worker runs all connections every 5 minutes, each in its own scope and try/catch — one person's
dead token never stalls another's sync. A booking or cancellation nudges the worker for that calendar
so the remote copy appears within seconds.

**Two processes, one database.** Blue/green keeps the previous slot *running* after a flip (both
were `active` when checked), so two copies of this worker exist at all times. Two safeguards:

- every run holds a **Postgres advisory lock** keyed on the calendar id for its whole length
  (`pg_try_advisory_lock` on a connection pinned for the run). Whichever process loses simply
  skips; the lock is released by the server itself if a process dies. Without this, each slot
  would push the same new appointment to Outlook once — two events for one visitor.
- the **standby slot stays quiet**: `Sync:ActiveSlotFile` points at `.active-env`, and a slot whose
  folder name does not match it skips the scheduled runs (it still honours nudges, which only
  arrive through requests, which only reach the serving slot). So the slot running last week's
  code after a deploy is not the one syncing.

## The OAuth flow

- `GET /api/sync/{provider}/connect?calendarId=…` (cookie session; the calendar's **own** owner
  only — the account owner cannot connect on an operator's behalf, because the account that signs
  in at Microsoft/Google would be the owner's, not the operator's). Builds a state token
  (Data Protection, time-limited 10 min, carrying tenant + user + calendar + provider + nonce) and
  redirects to the provider with `prompt=select_account` (the Email KB's lesson: without it the
  browser's current account is bound silently).
- `GET /api/sync/{provider}/callback?code&state` — anonymous by necessity; identity comes from the
  verified state, never from the query. Exchanges the code, asks the provider who signed in
  (`/me` on Graph, `calendars/primary` on Google), stores the encrypted tokens, nudges a first sync,
  and bounces to `/calendars?connected=microsoft` (or `?connectError=…`).
- `GET /api/sync/connections` — status per calendar for the UI.
- `POST /api/sync/connections/{calendarId}/sync-now` — runs one sync inline, returns the result.
- `POST /api/sync/connections/{calendarId}/disconnect` — deletes the mirrored blocks, forgets the
  remote ids on appointments (the events stay in the person's calendar — they are theirs now),
  revokes the Google token (Microsoft offers no revoke endpoint; the row is deleted).
- `GET /api/sync/providers` — which providers this server has credentials for. A provider with no
  client id shows as "not set up on this server" rather than a button that fails.

Scopes: Microsoft `offline_access Calendars.ReadWrite User.Read`; Google
`https://www.googleapis.com/auth/calendar.events` (read + write events on the primary calendar;
the primary calendar's id is the account email, so no extra profile scope is needed).

## Secrets and the blue/green trap

Tokens are encrypted with ASP.NET Data Protection. Two things make that safe across deploys:

1. **The key ring lives outside the deploy folders**: `/var/www/docucalendar/.aspnet/DataProtection-Keys`
   (the service user's home; rsync never touches it). The path is explicit in config
   (`DataProtection:KeyRingPath`) so nobody has to know the default.
2. **`SetApplicationName("docucalendar")`** — without it, ASP.NET derives the application identity
   from the content-root path, and `api-blue` and `api-green` are different paths. A token encrypted
   by one slot would be **unreadable by the other after the very next deploy**. This one line is
   the difference between sync surviving deploys and every connection dying every deploy. (The
   session cookies keyed the old way become invalid once; SSO signs people straight back in.)

Provider credentials go in the server's `appsettings.Production.json` (both slots) under `Sync:` —
never in the repository, same rule as the master key:

```json
"Sync": {
  "PublicBaseUrl": "https://calendar.docurest.com",
  "IntervalMinutes": 5,
  "Microsoft": { "ClientId": "", "ClientSecret": "", "Authority": "https://login.microsoftonline.com/common" },
  "Google":    { "ClientId": "", "ClientSecret": "" }
}
```

## What the owner does in the two consoles (the only manual steps)

**Google** (reusing the Sign-In client):
1. Google Cloud Console → *APIs & Services → Library* → enable **Google Calendar API**.
2. *Credentials* → the OAuth client `872977738839-…` → **Authorized redirect URIs** → add
   `https://calendar.docurest.com/api/sync/google/callback`.
3. *OAuth consent screen* → scopes → add `…/auth/calendar.events`.
   - If the consent screen's publishing status is **Testing**, add each staff Google address as a
     test user — and know that Google expires refresh tokens after **7 days** in Testing, so
     everyone would reconnect weekly. Publishing to *In production* removes that; the "unverified
     app" warning that appears until Google verifies the scope is a click-through for staff.
4. Copy the client id + secret (the same pair Docurest uses under `Authentication:Google`) into the
   calendar server's `Sync:Google`.

**Microsoft** (new app registration — nothing to reuse):
1. Entra admin center → *App registrations → New registration*: name **DocuCalendar**; supported
   account types **Accounts in any organizational directory and personal Microsoft accounts**;
   redirect URI type **Web**, value `https://calendar.docurest.com/api/sync/microsoft/callback`.
2. *Certificates & secrets → New client secret* — choose the longest expiry offered (24 months) and
   **write the expiry date down**; when it lapses, every Microsoft sync stops with `reconnect`.
3. *API permissions → Add → Microsoft Graph → Delegated*: `Calendars.ReadWrite`, `User.Read`,
   `offline_access`. No admin consent needed for these — each person consents for themselves.
4. Copy **Application (client) ID** and the secret **value** into the server's `Sync:Microsoft`.

## UI

- **Calendars** page, per calendar: a *Sync* line. Not connected → two buttons, *Connect Outlook 365*
  / *Connect Google Calendar* (shown only on the viewer's own calendars; other people's rows say
  "Behzod connects their own"). Connected → provider badge, the account email, last sync time and
  counts, *Sync now*, *Disconnect*. Status `reconnect` shows in amber with a *Reconnect* button.
- **Schedule** page: mirrored busy time shows the provider's mark and either its title (own
  calendar) or "Busy". The remove button is not offered on mirrored blocks (the API already refuses).
- **Guide**: a fifth, optional step — connect your calendar.

## Docurest side (small)

The booking webhook handler learns an `event` field: `booked` (today's message), `moved`
("📅 Appointment moved · was Fri 11 Sep 09:00 → now Fri 11 Sep 11:00 · changed in Outlook by …"),
`cancelled`. Same signature, same endpoint, same Telegram/in-app fan-out.

## Tests

- `SyncPlannerTests`: remote busy mirrored; free/transparent skipped; our own pushed event not
  mirrored; moved remotely → appointment moved; deleted remotely → appointment cancelled; stale
  mirrored block removed; block outside the window untouched; all-day event blocks the local day.
- `SyncStateTokenTests`: round trip; tampered → rejected; expired → rejected.
- Provider parsing tests on captured JSON shapes (Graph `calendarView`, Google `events.list`):
  times land in UTC, cancelled/transparent flags read correctly.

## Prove it (after deploy)

1. Connect the owner's Google calendar; create "Dentist 10:00–11:00" in Google → within 5 min the
   schedule shows it as busy (title visible to the owner), and `GET /api/booking/slots` no longer
   offers 10:00–11:00.
2. Book by phone → the appointment is in Google within seconds, with the visitor's number in the body.
3. Move that event in Google to 14:00 → the appointment here moves; Telegram announces the move.
4. Delete it in Google → cancelled here; Telegram announces the cancellation.
5. Repeat 1–2 with Outlook once the app registration exists.
6. Deploy again (any commit) → the connection still syncs (the `SetApplicationName` proof).

## Not in this phase

Choosing a non-primary remote calendar; mirroring attendees; syncing appointments *edited* here
(there is no edit here, only cancel); Outlook categories/colours; push notifications from the
providers instead of polling (5-minute polling is what was asked for and is plenty).
