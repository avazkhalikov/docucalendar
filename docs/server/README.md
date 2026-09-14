# Server files for white-label portals

Copies of what runs on the server, kept here because a file that exists only on one machine is a
file that disappears the day that machine is rebuilt. **These are copies, not the source of
truth** — editing one here changes nothing until it is put back on the server.

| File here | Lives on the server at |
|---|---|
| `nginx-docucalendar-path.conf` | `/etc/nginx/snippets/docucalendar-path.conf` |
| `nginx-portal-app.conf` | `/etc/nginx/snippets/portal-app.conf` |
| `portal-sync.sh` | `/usr/local/bin/portal-sync.sh` |
| `portal-sync.systemd` | `/etc/systemd/system/portal-sync.{service,timer}` (two units in one file here) |

Secrets are **not** here and must not be added: `/etc/portal-sync.env` holds the job token, and
`/etc/systemd/system/ragstudio-api.service.d/portal.conf` holds the application's copy of it. The
two must match; regenerate both together if either is ever rotated.

## How a customer domain becomes a working site

1. A global admin adds the host in **Docurest → White labels**, with the account it serves and the
   brand it shows.
2. `portal-sync.sh` (every two minutes) asks `GET /api/portal/hosts` for the enabled hosts and,
   for each one it does not already serve:
   - **skips it entirely** if some hand-written block already declares that `server_name` — a
     generated duplicate would leave nginx choosing between them by filename;
   - obtains a certificate with certbot, but **only if the host's DNS resolves to this server**.
     A customer who keeps their own reverse proxy in front holds their own certificate, and
     asking Let's Encrypt for one we cannot validate spends a rate limit for nothing;
   - writes `/etc/nginx/sites-enabled/portal-<host>.conf` — with a TLS block when we hold a
     certificate, and a plain port-80 block when the customer terminates TLS themselves — then
     tests and reloads nginx.
3. A host removed from the table loses its generated block on the next run.

Both generated shapes include `snippets/portal-app.conf`, which is the whole portal: the
brand-neutral app shell, the API, the live connection, the widget, and the calendar under
`/calendar/`. Nothing in it is host-specific — the brand comes from `/api/portal/branding` — so
one file serves every customer.

## Reading the log

`/var/log/portal-sync.log`, and `journalctl -u portal-sync.service`. The lines worth knowing:

- `skipping <host>: served by a hand-written block` — normal for the portals that predate this.
- `obtaining a certificate for <host>` followed by `certificate obtained` — a new customer came up.
- `certbot failed for <host>` — usually DNS pointing somewhere else, or port 80 not reaching us.
- `NGINX CONFIG INVALID — not reloading` — the generated file is wrong. nginx keeps running with
  the previous configuration; fix the template before anything else.
