# Deploy PharmaPOS dashboard (`mypos.cloudpharma.site`)

## Prerequisites

- Node.js 20+ and pm2 on the VPS
- MySQL database `pharmapos_reporting` with reporting schema + `docs/mysql/dashboard_tenant_auth.sql`
- DNS A record: `mypos.cloudpharma.site` → VPS IP
- Nginx + Certbot for TLS

## Steps

1. Copy `web/` to `/var/www/mypos` (excluding `node_modules`).
2. Create `/var/www/mypos/.env.production` from `.env.example` (never commit secrets).
3. On the server:

```bash
cd /var/www/mypos
npm ci
npm run build
pm2 start deploy/ecosystem.config.cjs
pm2 save
```

4. Install nginx site from `deploy/nginx-mypos.conf`, then:

```bash
certbot --nginx -d mypos.cloudpharma.site
nginx -t && systemctl reload nginx
```

5. In POS Settings → Preferences, enable MySQL sync.
   Set `ReportingSync:RealtimeSecret` (appsettings or `%LocalAppData%\PharmaPOS\mysql-sync-settings.json`)
   to the value in `/root/mypos-realtime-secret.txt` on the VPS.
   `DashboardNotifyUrl` should be `https://mypos.cloudpharma.site/api/realtime/notify`.

Default owner login (change after first login): `owner@cloudpharma.site` / `Owner@123`
