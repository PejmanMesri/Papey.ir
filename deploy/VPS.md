# Papey VPS operations

Deployed on 2026-10-17 to `89.44.240.75` (Ubuntu 26.04, 2 vCPU, 2 GB RAM).
The live domain is **https://papey.ir/** (and https://www.papey.ir/). This release
includes phone-first signup, explicit homepage product slots, SMS, and unlimited
inventory. Render remains untouched; its customer/order data was not imported.
Demo products have now been removed from the VPS catalog.

## Layout

- App: self-contained .NET 10 Linux x64 release at `/opt/papey/current`, managed
  by `papey.service`, running as the unprivileged `papey` user.
- PostgreSQL: `papeydb`, local connections only on port 5432.
- Application configuration: `/etc/papey/papey.env`, readable only by root.
- Private print files and data-protection keys: `/var/lib/papey/private`.
- Uploaded images/videos: `/var/lib/papey/uploads`.
- Reverse proxy: `/etc/nginx/sites-available/papey` and `papey-ip`.
- Authoritative DNS: BIND, `/etc/bind/db.papey.ir`, recursion disabled.
- Firewall: SSH, HTTP/HTTPS, and TCP/UDP DNS allowed. App port 5080 and
  PostgreSQL are bound only to localhost. A 2 GB swap file is enabled.

## DNS and TLS

IRNIC has `ns1.papey.ir` and `ns2.papey.ir`, both with glue `89.44.240.75`.
Both nameservers currently run on this single VPS: this does not provide DNS
redundancy. An independent secondary DNS service is a future improvement.

`papey.ir` and `www.papey.ir` have A records pointing to the VPS. Public DNS is
active, and the domain certificate was issued successfully on 2026-10-17.

`papey-https.timer` issued the domain certificate, enabled HTTPS redirection,
and disabled itself after success.
Check its results with `journalctl -u papey-https.service`.

The IP preview `https://89.44.240.75` has a publicly trusted, short-lived
Let's Encrypt certificate. Certbot 5.8 is installed in `/opt/papey-certbot`.
`certbot.timer` renews certificates, with an nginx reload deploy hook for the IP.
The domain callback is `https://papey.ir/payment/callback`; do not use the IP
preview to test real payments.

## Access and checks

From this Windows account:

```powershell
ssh -i "$env:USERPROFILE/.ssh/papey_deploy_key" root@89.44.240.75
```

The initial root password was expired and has been rotated. The replacement is
saved privately in `%USERPROFILE%/.ssh/papey-vps-root.txt`. The generated app
admin password is in `%USERPROFILE%/.ssh/papey-admin-password.txt` (username
`Admin`). These files have Windows ACLs restricted to the current user. Never
copy these files into the project, commits, screenshots, or chat messages.

Useful server commands:

```sh
systemctl status papey nginx named postgresql
journalctl -u papey -n 60 --no-pager
curl -fsS http://127.0.0.1:5080/healthz
dig @127.0.0.1 papey.ir A
/opt/papey-certbot/bin/certbot certificates
```

Verified: Release Linux publish; 8 deployment, 21 inventory, 7 catalog/migration,
and 37 SMS/authentication checks;
database migrations; public health/homepage; HTTPS product selection and cart;
HTTPS admin login. Production SMS and real payment transactions were not tested.
The certificate renewal dry run also passed. Both the domain and IP preview use
trusted HTTPS certificates with automatic renewal.
Production SMS.ir credentials and the assigned sender line are configured. API
credit and sender-line checks passed. With no approved Verify template configured,
OTP uses the assigned sender line; delivery to blocked advertising numbers may
require an approved service/Verify template. Payment remains in sandbox mode.

## Homepage products and sign-in

Login and signup open with the phone-number method first. Verification creates
an ordinary account only after a valid SMS code; existing phone numbers sign in
to their existing account. Username/password is the second method. Manual
username registration remains available at `/register/password`.

To set the two products pictured in the top of the homepage:

1. Open **Products** in the admin panel and create or edit your own product.
2. Upload its main image in the media section. The first image by display order
   is the main image used on the homepage.
3. Under **تصویر بالای صفحه اصلی**, choose **جایگاه اول** or **جایگاه دوم**.
4. Keep the product active and click **ذخیره اطلاعات**. Repeat for the second
   product. Selecting an occupied slot replaces its previous product.

Startup seeds only lookup values and categories. Demo products are removed by
the cleanup migration when unused; demo products referenced by historical
orders are archived instead. Customized products with their own uploaded images
are preserved. The homepage shows an empty brand panel until you select products.

## Backups

`/usr/local/sbin/papey-backup` runs daily at 00:15 UTC (03:45 Tehran), retaining
approximately seven days in `/var/backups/papey`. Each backup includes a custom
PostgreSQL dump, uploads/private files, app environment, and server configuration
(including TLS certificate files).
Directories are accessible only by root. The initial backup was copied to
`%USERPROFILE%/.ssh/papey-initial-backup.tar.gz` with a restrictive ACL.

Daily backups currently stay on the VPS; the initial local copy is the only
off-server copy. Arrange independent recurring backup storage for ongoing sales.

## Updating safely

Build outside the repository with `dotnet publish Papey.csproj -c Release -r
linux-x64 --self-contained true -o <temporary-release-directory>`. Archive only
the published output; environment files, App_Data, and deploy-artifacts are
excluded by the project file. Upload to a new dated release directory, retain
the persistent-data symlinks, take a database backup, update `/opt/papey/current`,
and restart `papey.service`. Check health and login before considering the
release successful. Keep the previous release for rollback; database migrations
may require restoring the pre-update backup as well.
