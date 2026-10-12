# Desktop order printing integration

Only successfully paid orders enter the queue. The PostgreSQL trigger installed by
`EnqueuePaidOrderPrintJobs` inserts one job in the same transaction that first saves
`Status = Paid`, `PaidAt`, and a nonempty payment reference. Pending and failed orders
are excluded; historical paid orders are not backfilled. Repeated payment callbacks
cannot create duplicate jobs. The desktop app is a separate project.

## Authentication and connection

Use HTTPS in deployment and preserve cookies in one cookie container shared by the
HTTP client, SignalR connection, and receipt WebView. This API uses the existing
administrator account; no shared API key or public receipt URL is needed.

1. `POST /api/admin/printing/login` with JSON
   `{ "identifier": "<admin username>", "password": "<admin password>" }`.
   Success is `204` plus an authentication cookie. Login is limited to five attempts
   per minute per source IP. Invalid credentials and non-admin accounts return `401`.
2. `GET /api/admin/printing/session` returns `csrfToken`, `csrfHeader` (`X-CSRF-TOKEN`),
   `userId`, `hubUrl`, `notification`, and `leaseSeconds` (120).
3. Include the CSRF token header on every subsequent POST, with the same cookies.
4. Connect a SignalR client to `/hubs/admin/printing` using those cookies. Subscribe
   to the no-argument `PrintJobsAvailable` event. Enable automatic reconnect.

The server checks the durable queue every two seconds. Events are wake-up hints,
not individual print jobs, and can repeat. Drain the queue on initial connection,
after reconnecting, and on each event. Also poll periodically (for example every
30 seconds) as a fallback. Use one serialized drain loop per desktop client.

## Queue protocol

| Method / path under `/api/admin/printing` | JSON body | Response |
| --- | --- | --- |
| POST `/jobs/claim` | `{ "clientId": "shop-printer-1" }` | `200` job lease, or `204` when no job is ready |
| POST `/jobs/{jobId}/renew` | `{ "clientId": "shop-printer-1", "leaseToken": "<guid>" }` | `200` with `leaseExpiresAt` |
| POST `/jobs/{jobId}/complete` | Same lease body | `204`; repeating the same acknowledgement is safe |
| POST `/jobs/{jobId}/release` | Same lease body, optionally `"error": "Printer offline"` | `204`; retries become available after 15 seconds |
| GET `/jobs/{jobId}` | None | `200` status, timestamps, attempts, client ID, last error; `404` if missing |
| POST `/logout` | None | `204`; close the SignalR connection too |

`clientId` must be a stable, nonempty installation identifier of at most 100
characters. `error` is optional and limited to 1,000 characters. Avoid credentials
and customer data in error messages. All IDs, lease tokens and dates are JSON
strings except numeric order/user IDs and attempt counts. Dates are UTC.

A successful claim returns:

```json
{
  "jobId": "<guid>",
  "orderId": 123,
  "orderNumber": "PY-...",
  "orderStatus": "Paid",
  "paidAt": "2026-10-11T10:00:00Z",
  "receiptUrl": "/admin/print/order/123?autoPrint=false",
  "leaseToken": "<guid>",
  "leaseExpiresAt": "2026-10-11T10:02:00Z",
  "attempts": 1
}
```

Open `receiptUrl` against the same server origin in the authenticated WebView.
This is the existing 80 mm admin order print page. `autoPrint=false` suppresses
its browser print dialog so the desktop app can control printing. Wait for
`.receipt[data-print-ready="true"][data-order-id="123"]`, fonts, and images to
finish loading before printing with print CSS. Confirm the expected order ID.

Renew at least every 30 seconds while preparing/printing, including immediately
before sending to the printer. A `409` means the lease is no longer valid or the
order is no longer eligible: stop that attempt. Other clients cannot claim an
unexpired lease. Cancelled/failed/pending orders cannot be newly claimed or renewed.

After the printer/spooler reports success, call `/complete`. On a confirmed failure
before printing, call `/release`. If a completion response is lost, retry it with
the same token. A late acknowledgement is accepted if the job has not been
reclaimed; a reclaimed job gets a new token and rejects stale acknowledgements.

The queue is durable and uses leases with retries. Physical printing is **at least
once**, not exactly once: a crash after paper prints but before acknowledgement
can cause a retry. The desktop app should keep a durable local job/spooler journal
and reconcile uncertain outcomes instead of blindly reprinting. An abandoned
lease expires after two minutes, allowing another client to recover the job.

`401` means sign in again; `403` means admin access was removed; `400` indicates
invalid input or a missing/expired CSRF token (refresh `/session`); `429` means wait
before retrying login. Transient server failures must be retried with backoff.

## Operations and validation

Normal app startup applies migrations. Keep PostgreSQL and `App_Data/data-protection`
durable; the Docker Compose setup already mounts `App_Data`. Ephemeral hosting can
invalidate login cookies after restart, so reconnect/re-authenticate as necessary.
Multiple app instances must share the database and cookie protection keys to share
sessions. Queue claiming coordinates through PostgreSQL across instances.

Run the build, deployment checks, and isolated PostgreSQL printing checks:

```powershell
dotnet build -c Release
dotnet run --project tests/DeploymentChecks -c Release
dotnet run --project tests/PrintingChecks -c Release
```

Printing checks read local `.env` database settings or `PAPEY_TEST_CONNECTION`,
create a temporary database, and remove only that database when finished. The
database account needs permission to create databases. They never place a real
order, call the payment gateway, or send anything to a printer.

References: [SignalR cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0),
[PostgreSQL queue locking](https://www.postgresql.org/docs/17/sql-select.html).
