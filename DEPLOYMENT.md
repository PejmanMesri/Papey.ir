# Papey free deployment

The full .NET 10 / Blazor Server app is configured in `render.yaml` for a free
Docker web service and a free PostgreSQL 17 database in Frankfurt. Deploy the
Blueprint from the deployment branch in the private GitHub repository.
No paid service, persistent disk, or payment method is required by this configuration.

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
- Render supplies an HTTPS `onrender.com` address. VPN-free access from Iran
  has not been verified; this can vary by ISP and network conditions.
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

SMS login and password reset require `KAVENEGAR_API_KEY`. Without a configured
provider, production fails closed and password login remains available. Set
`KAVENEGAR_SENDER` and `ADMIN_PHONES` only if needed. Development can still show
an OTP locally. Password registration cannot assign an administrator role merely
by supplying an administrator's phone number.

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
