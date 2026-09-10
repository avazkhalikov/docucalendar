# One-time server setup — calendar.docurest.com

Run once on the Hetzner box (`root@77.42.95.87`). After this, every deploy is `git push`.

Ports **5014** (blue) and **5015** (green). BlogForge holds 5012/5013; Docurest holds 5000.

## 1. User, directories, database

```bash
adduser --system --group --home /var/www/docucalendar docucalendar
mkdir -p /var/www/docucalendar/{api-blue,api-green,client-blue,client-green,keys}
echo blue > /var/www/docucalendar/.active-env
ln -sfn /var/www/docucalendar/api-blue   /var/www/docucalendar/api
ln -sfn /var/www/docucalendar/client-blue /var/www/docucalendar/client
chown -R docucalendar:docucalendar /var/www/docucalendar

# Database (on the DB host, 10.0.0.2)
psql -h 10.0.0.2 -U postgres -c "CREATE DATABASE caldb;"
psql -h 10.0.0.2 -U postgres -c "CREATE USER docucalendar WITH PASSWORD 'CHOOSE-A-STRONG-ONE';"
psql -h 10.0.0.2 -U postgres -c "GRANT ALL PRIVILEGES ON DATABASE caldb TO docucalendar;"
psql -h 10.0.0.2 -U postgres -d caldb -c "GRANT ALL ON SCHEMA public TO docucalendar;"
```

## 2. Secrets — on the server only, never in git

Write the SAME file into both slots (`api-blue` and `api-green`):

```bash
cat > /var/www/docucalendar/api-blue/appsettings.Production.json <<'JSON'
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=10.0.0.2;Database=caldb;Username=docucalendar;Password=CHOOSE-A-STRONG-ONE"
  },
  "Docurest": {
    "MasterKey": "GENERATE-64-CHARS",
    "SsoSecret": "GENERATE-64-CHARS-DIFFERENT",
    "WebhookUrl": "https://docurest.com/api/calendar/webhooks/booked"
  },
  "Cors": { "AllowedOrigins": [ "https://calendar.docurest.com" ] }
}
JSON
cp /var/www/docucalendar/api-blue/appsettings.Production.json /var/www/docucalendar/api-green/
chown docucalendar:docucalendar /var/www/docucalendar/api-*/appsettings.Production.json
chmod 600 /var/www/docucalendar/api-*/appsettings.Production.json
```

Generate each secret with `openssl rand -base64 48`. **The same two values go into Docurest's own
configuration** (`Calendar:MasterKey`, `Calendar:SsoSecret`) — that shared pair is the whole trust
relationship between the two services.

## 3. systemd — one unit per slot

```bash
for SLOT in blue:5014 green:5015; do
  NAME=${SLOT%%:*}; PORT=${SLOT##*:}
  cat > /etc/systemd/system/docucalendar-api-$NAME.service <<EOF
[Unit]
Description=DocuCalendar API ($NAME)
After=network.target

[Service]
WorkingDirectory=/var/www/docucalendar/api-$NAME
ExecStart=/usr/bin/dotnet /var/www/docucalendar/api-$NAME/DocuCalendar.WebApi.dll
Restart=always
RestartSec=5
User=docucalendar
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:$PORT
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF
done
systemctl daemon-reload
systemctl enable --now docucalendar-api-blue docucalendar-api-green
```

## 4. nginx + TLS

```bash
cat > /etc/nginx/conf.d/docucalendar-upstream.conf <<'EOF'
upstream docucalendar_backend { server localhost:5014; }
EOF

cat > /etc/nginx/sites-available/calendar.docurest.com <<'EOF'
server {
    listen 80;
    server_name calendar.docurest.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name calendar.docurest.com;

    # certbot fills these in
    ssl_certificate     /etc/letsencrypt/live/calendar.docurest.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/calendar.docurest.com/privkey.pem;

    root /var/www/docucalendar/client;
    index index.html;

    # The SPA owns every path that is not the API — deep links must not 404 on refresh.
    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://docucalendar_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
EOF
ln -sfn /etc/nginx/sites-available/calendar.docurest.com /etc/nginx/sites-enabled/
nginx -t && nginx -s reload
certbot --nginx -d calendar.docurest.com
```

DNS first: an `A` record for `calendar` → `77.42.95.87`, or certbot will fail.

## 5. GitHub secrets

The workflow needs `HETZNER_HOST`, `HETZNER_USER`, `HETZNER_SSH_PRIVATE_KEY` — the same three
BlogForge uses.

## 6. Prove it

```bash
curl -s https://calendar.docurest.com/api/health     # {"status":"ok","app":"docucalendar"}
```

Then from Docurest: open **My Calendar** in the sidebar. First arrival registers the account and
lands on the Calendars page.
