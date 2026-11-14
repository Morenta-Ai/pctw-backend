# Architecture

## Shape

```
clients ──HTTPS/JSON──▶ Rethread.Api ──EF Core/Npgsql──▶ PostgreSQL (Neon)
```

One ASP.NET Core minimal API project, organised by audience and feature:

```
src/Rethread.Api/
  Program.cs            builder → AddRethread() → UseRethread() → MapRethread()
  Hosting/              service registration and the request pipeline
  Common/               errors, JSON, time, IDs, paging, validation, geo, money
  Auth/                 tokens, OTP, staff passwords, policies
  Data/                 AppDbContext, entities, configurations, migrations, dev seed
  Domain/               business rules shared by the three audiences
  Background/           periodic maintenance (offer expiry, waiting-pickup alerts)
  Features/Customer/    /api/v1/…            (pctw-user)
  Features/Collector/   /api/v1/collector/…  (pctw-collector)
  Features/Admin/       /api/v1/admin/…      (pctw-admin)
tests/Rethread.Api.Tests/
```

## Request flow

endpoint (route group, policy, validation filter) → `Domain` service or a query on `AppDbContext` →
response record that mirrors the contract.

Endpoints are thin. Anything another audience could also trigger (cancelling a pickup, crediting a
wallet, sending a notification) goes through a `Domain` service, so the user app, collector app and portal
can't disagree about what a cancel or a collection does.

## One pickup, three views

A pickup is one row in `pickups`. Its status is the portal's set: `pending`, `assigned`, `enRoute`,
`arrived`, `collected`, `deliveredToHub`, `cancelled`. The response mappers translate:

| Stored | User app | Collector app |
| --- | --- | --- |
| `pending` | `pending` | `open` |
| `assigned` | `assigned` | `accepted` |
| `enRoute` | `enRoute` | `onTheWay` |
| `arrived` | `enRoute` | `arrived` |

The rest are the same everywhere.

## Domain services

| Service | Owns |
| --- | --- |
| `PickupWorkflow` | Booking, accepting, starting, arriving, collecting, hub drops and cancelling |
| `AssignmentService` | Call-centre offers: 45-second expiry, accept, decline, withdraw |
| `OrderWorkflow` | Placing orders (stock and wallet) and the fulfilment status table |
| `Redemptions` | Cash, vouchers and donations; staff paying or rejecting transfers |
| `WalletLedger` | The signed ledger, balances, and the per-customer lock for spending |
| `RateBook` | Which rates apply on a Colombo day |
| `ImpactCalculator` | Kilograms × multipliers for the impact screen |
| `NotificationService`, `NotificationTexts` | Alerts for all three audiences, and their wording |
| `Maintenance` | Offer expiry, 30-minute waiting alerts, silent collectors going offline |

Money moves under database guarantees: accepting a job and buying stock are conditional updates, and
wallet spending takes a PostgreSQL advisory lock per customer, so concurrent requests can't double-spend.

## Background work

`MaintenanceService` runs `Maintenance` every few seconds. Every read that depends on it (an offer's
status, the dispatch map, the counts) also runs it first, so the API is correct even where background work
doesn't run, such as on a platform that sleeps between requests.

## Errors

Every non-2xx response is `{ code, message, details }`:

- Endpoints and services throw `ApiException` (`ApiException.Conflict("JOB_TAKEN", …)`).
- `ApiExceptionHandler` renders it; anything else becomes 500 `INTERNAL_ERROR` and is logged.
- `UseContractStatusCodes` gives bodiless statuses (401 from the JWT handler, 404 for unknown routes) the
  same body.
- The validation filter answers 422 `VALIDATION_FAILED` with `details` mapping field names to messages.

## Time

Inject `TimeProvider` and call `time.UtcNow()`. Tests swap in a `FakeTimeProvider` fixed at
2026-11-07 06:00 UTC (11:30 in Colombo) and advance it. "Today", "this week" and slot windows are Colombo
calendar days.

## Data

- PostgreSQL through EF Core with snake_case names. Migrations live in `Data/Migrations`; Development
  applies them on start, production applies them as a deploy step.
- Money is `bigint` cents, weights `numeric(8,2)`, times `timestamptz`.
- Human-readable IDs (`RT-24817`, `ORD-5134`, `COL-0457`) come from PostgreSQL sequences. Other IDs are
  opaque prefixed strings (`cus_…`, `asg_…`).

## Tests

xUnit v3 on Microsoft.Testing.Platform. `PostgresServer` (an assembly fixture) owns one server and a
migrated template database; `TestApi.StartAsync` clones the template so every test has its own database
and its own fake clock. `ShapeAssert` compares responses with the contract samples.
