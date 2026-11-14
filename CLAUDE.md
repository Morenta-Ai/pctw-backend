# ReThread API

ASP.NET Core 10 minimal API for the PCTW platform. It serves three clients from one PostgreSQL database
(Neon in production): the user app at `/api/v1`, the collector app at `/api/v1/collector` and the admin
portal at `/api/v1/admin`. The clients never connect to the database.

Read `docs/ARCHITECTURE.md` before structural changes. The clients' contracts in `docs/contracts/` are the
spec: read the relevant one before adding or changing an endpoint.

## Rules

- Responses match the contract field for field. Add a ShapeAssert test (`MatchesContract`) for every
  endpoint with a response sample, and exercise every documented error code.
- A contract change starts in the client repo. Run `scripts/sync-contracts.sh` and keep the copies in step.
- Every error is `{ code, message, details }` via `ApiException`. Never return `Results.Problem` or bare
  status codes from endpoints.
- Money is `long` cents (`…Cents`), never `double` or `decimal` money. Weights are `decimal` kg (`…Kg`),
  2 decimals. Use `Money.Payout` for weight × rate.
- Times are `DateTime` in UTC (`TimeProvider.UtcNow()`); never `DateTime.Now`/`UtcNow` directly, so tests
  can move the clock. Calendar days are Colombo days (`Colombo.Today`, `Colombo.DateOf`).
- Rates and impact multipliers come from the database, never constants.
- Business rules shared by audiences live in `Domain/`, not in endpoints. A pickup is one row: the
  customer sees a pickup, the collector a job, the portal a pickup request.
- Each audience's endpoints sit in their own route group with their own authorisation policy.
- Validate request bodies with a FluentValidation validator and `.Validate<T>()`.
- Never return a customer's full bank account number outside the staff redemption queue.
- Anything that spends wallet money takes `WalletLedger.LockAsync` inside a transaction.
- Time-based rules (offer expiry, waiting alerts) live in `Maintenance`; run it before reads that depend
  on them rather than trusting the background job.
- The seed must keep matching the portal's mock world (`tests/.../Seed/*.json`). Don't change seed data
  without regenerating those fixtures from pctw-admin.

## Commands

```sh
dotnet format          # before committing; CI runs --verify-no-changes
dotnet build
dotnet test            # TEST_DATABASE_URL, or Docker for Testcontainers
dotnet ef migrations add <Name> --project src/Rethread.Api --output-dir Data/Migrations
dotnet run --project src/Rethread.Api -- seed --reset   # fresh dev data, dated today
dotnet run --project src/Rethread.Api -- migrate        # what production runs on deploy
```

## Git

Work on a branch (`feature/<name>`, `fix/<name>`, `chore/<name>`), never directly on `main`, and use
Conventional Commits. See `docs/CONVENTIONS.md`.
