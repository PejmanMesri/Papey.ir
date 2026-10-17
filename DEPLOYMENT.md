# Papey free deployment

The full .NET 10 / Blazor Server app is configured in `render.yaml` for a free
Docker web service and a free PostgreSQL 17 database in Frankfurt. Deploy the
Blueprint from the deployment branch in the private GitHub repository.
No paid service, persistent disk, or payment method is required by this configuration.

## Current deployment (2026-10-11)

- Address: https://papey-store.onrender.com
- Render status: Live, commit `31b622c11147f6ba03421db185690709b1f9ce39`.
- Branch: `codex/free-hosting-20261011`.
- Both resources use the free plan. Database expiry: **2026-11-10**.
- Render's container build, database startup, and deployment health check passed.
  Local deployment checks and Release publish passed before deployment.
- Public storefront verification is blocked on this computer: DNS resolves the
  address to private IP `10.10.34.36`, and HTTPS connections are refused.
  VPN-free access on this network is unavailable; storefront/cart browser checks
  could not be completed. No system network settings were changed.

## Important plan limits

- This is a temporary test deployment. Render's free Postgres database expires
  30 days after creation, becomes inaccessible, and is subsequently deleted.
  Export the database before expiry or move it to another provider.
- The free web service sleeps after 15 minutes without traffic and may take
  approximately one minute to wake. Blazor circuits must reconnect on restart.
- New uploads, private design files, and data-protection keys are on ephemeral
  storage and are lost when the service restarts, redeploys, or sleeps. Existing
  media included in the source remains available. Login cookies and in-memory
  carts may be invalidated on restart. Use durable storage before live sales.
- Render supplies an HTTPS `onrender.com` address. This computer's network cannot
  reach the address; access from other Iranian ISPs remains unverified.
- Liara was checked on 2026-10-11: the signed-in account showed paid .NET/Docker
  plans, zero credit, and a provider subdomain limited to seven days. No Liara
  service was created.

## Deployment

1. Grant Render access to **only** `PejmanMesri/Papey.ir` in the existing GitHub
   integration. Keep the repository private.
2. Create a Blueprint from that repository and select the deployment branch.
   Use `render.yaml` at the repository root.
3. Verify both resources use the `free` plan before deploying.
4. Render injects `DATABASE_URL`, generates `ADMIN_PASSWORD`, and exposes the
   application on Render's default port 10000. Database migrations and sample store seeding run
   at startup. The database blocks public connections by default.
5. Wait for the deployment to become live. Check `/healthz`, the storefront,
   the Blazor connection, and login. The admin username is `Admin`; retrieve
   its generated password privately in Render's environment settings.

## Optional integrations

Payments remain in Zarinpal sandbox mode. The callback defaults to the
host-generated `RENDER_EXTERNAL_URL` plus `/payment/callback`. Set
`ZARINPAL_CALLBACK_URL` explicitly if using another provider or a custom domain.
Real payments require your own merchant credentials and gateway eligibility;
the test merchant ID in the Blueprint does not establish a real payment account.

SMS login, password reset, shipping notifications, and admin bulk sends use SMS.ir.
Set `SMSIR_API_KEY` as a secret and `SMSIR_SANDBOX=false` for real production
sends. Set `SMSIR_LINE_NUMBER` to your assigned dedicated sender line. Set
`SMSIR_OTP_TEMPLATE_ID` and `SMSIR_SHIPPING_TEMPLATE_ID` after SMS.ir approves
the templates; until then production OTP/shipping use the dedicated line.
Template parameters are `CODE` and `ORDERNUMBER`, respectively. Dedicated-line
messages may be rejected for recipients who block advertising messages; Verify
templates are recommended for transactional messages.

Development always selects `SMSIR_SANDBOX_API_KEY` and the sandbox Verify
template `123456`. Sandbox sends never deliver real messages or consume credit.
Only Development can display the sandbox OTP; Production with sandbox enabled
refuses OTP login, keeping password login available. Never commit either API key.
The ignored local `.env` contains local secrets; hosted services must receive
secrets through their environment settings. `ADMIN_PHONES` is optional for
existing paid-order notifications.

The mobile-friendly `/admin/sms` page previews bulk recipients/text before
sending, removes duplicate numbers, and reports provider acceptance/rejection
counts. Limit: 100 recipients / 1000 characters per batch. Provider acceptance
is not a delivery confirmation. No automatic bulk retry occurs.

Shipping messages are queued in PostgreSQL atomically when admin changes an
order to Shipped. A unique OrderId prevents duplicate messages on repeated
updates. Definitively rejected sends retry up to three times; administrators
can retry failures from `/admin/sms`. Timeouts, malformed responses, and interrupted
sends are marked Unknown instead of automatically risking a duplicate SMS.
Check SMS.ir reports before manually resending those. Jobs retain their sandbox
mode and cannot accidentally send using production credentials after an environment
switch. Historical shipped orders are not backfilled by the migration.

Suggested production templates (submit in SMS.ir's fast-send template panel):
- OTP: `پاپی؛ کد ورود شما: #CODE#`
- Shipping: `پاپی؛ سفارش #ORDERNUMBER# شما ارسال شد.`

Render's HTTPS-only ingress proxies HTTP to the container. The Blueprint enables
ASP.NET forwarded headers for that environment and secure-only authentication
cookies. Do not reuse this proxy-trusting configuration on a publicly exposed
container without restricting the upstream proxy.

For another PostgreSQL host, supply a native Npgsql connection string in
`ConnectionStrings__DefaultConnection`, or an ordinary PostgreSQL `DATABASE_URL`.
The URL adapter requires TLS; advanced SSL/certificate/query options should use
the explicit connection string. Separate `DB_HOST`, `DB_PORT`, `POSTGRES_DB`,
`POSTGRES_USER`, and `POSTGRES_PASSWORD` continue to work for local Docker.

## Verification

```powershell
dotnet run --project tests/DeploymentChecks/DeploymentChecks.csproj -c Release
dotnet run --project tests/SmsChecks/SmsChecks.csproj -c Release
dotnet publish Papey.csproj -c Release -o "$env:TEMP/papey-deployment-check"
```

The checks cover missing, guessed, replayed, expired, and concurrently redeemed
OTP handoff tickets, plus managed PostgreSQL URL parsing and credential escaping.
The container build verifies the published Blazor framework JavaScript exists.

Keep `.env`, `App_Data`, generated build folders, and local credentials out of
source archives and container build contexts. Do not upload your local database
or admin credentials. This deployment starts with the project's seeded catalog.

Provider references:
- [Render free limits](https://render.com/docs/free)
- [Render Blueprint reference](https://render.com/docs/blueprint-spec)
- [Render Docker hosting](https://render.com/docs/docker)
- [Liara Docker deployment](https://docs.liara.ir/paas/docker/how-tos/deploy-app/)

SMS checks use a disposable PostgreSQL database and a simulated SMS provider.
Set `PAPEY_SMS_SANDBOX_CHECK=true` to additionally exercise real SMS.ir sandbox
Verify/bulk requests; never use this switch to test production sends.
On 2026-10-12, 34 automated SMS checks passed, along with the existing deployment
and printing checks. A live sandbox Verify request was accepted. Later live
requests, including the admin bulk UI check inside Docker, encountered intermittent
DNS/transport failures reaching api.sms.ir. The UI reported an unknown result
without automatically retrying. Real production messages were not sent during testing.
