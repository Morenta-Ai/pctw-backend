# ReThread API

ASP.NET Core API for the PCTW (Post-Consumer Textile Waste) platform. One server, one PostgreSQL database,
three clients:

| Client | Repo | Base path |
| --- | --- | --- |
| User app (Flutter) | [pctw-user](https://github.com/Morenta-Ai/pctw-user) | `/api/v1` |
| Collector app (Flutter) | [pctw-collector](https://github.com/Morenta-Ai/pctw-collector) | `/api/v1/collector` |
| Admin & call-centre portal (Next.js) | [pctw-admin](https://github.com/Morenta-Ai/pctw-admin) | `/api/v1/admin` |

Each client's `docs/API_CONTRACT.md` is the agreement this API implements. Copies live in
[`docs/contracts/`](docs/contracts), and the tests check responses against their samples.

## Setup

You need the .NET 10 SDK and PostgreSQL 17.

```sh
docker compose up -d          # PostgreSQL on localhost:5432 (user, password and database: rethread)
dotnet run --project src/Rethread.Api
```

Without Docker, any local PostgreSQL works if it has a `rethread` role (password `rethread`) that owns a
`rethread` database. In Development the API applies migrations and seeds an empty database on start. It
listens on http://localhost:5080, with the API reference at http://localhost:5080/docs and health at
`/health`.

## Seed data

Development and the tests use the same data as the clients' mock modes: a port of the admin portal's mock
world, so `RT-24841`, `COL-0457`, `ORD-5134` and the rest line up across all three apps. Times are relative
to when the seed ran, so today's queue looks like today. Re-seed to move it to now:

```sh
dotnet run --project src/Rethread.Api -- seed --reset
```

| Who | Sign in with |
| --- | --- |
| Admin | `admin@rethread.lk` / `rethread-admin` |
| Call centre | `callcentre@rethread.lk` / `rethread-callcentre` |
| Collector Ruwan Silva | code `COL-0457`, mobile `+94771234567`, code `123456` in Development |
| Customer Nadeesha Perera | mobile `+94764821190`, code `123456` in Development |

`tests/Rethread.Api.Tests/Seed/portal-mock-world.json` pins the seed to the portal's mock world, and
`portal-operations.json` pins the portal's dashboard, lists, dispatch map and alerts to what its mock
sources show. If the portal's mock changes, regenerate them (each test says how) and update the API to
match.

## Tests

```sh
dotnet test
```

Each test runs against its own database, cloned from a migrated template. By default the tests start
PostgreSQL with Testcontainers, which needs Docker. To use a server you already have instead, point
`TEST_DATABASE_URL` at it with a role that can create databases:

```sh
export TEST_DATABASE_URL="Host=localhost;Port=5432;Username=rethread;Password=rethread;Database=postgres"
```

## Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Default` | local `rethread` database in Development | PostgreSQL (Neon in production) |
| `Database:MigrateOnStartup` | `true` in Development | Apply migrations when the API starts |
| `Seed:OnStartup` | `true` in Development | Seed an empty database when the API starts (never in Production) |
| `Jwt:SigningKey` | a dev key in Development | HS256 key for access tokens, at least 32 characters. Required |
| `Jwt:AppAccessTokenMinutes`, `Jwt:StaffAccessTokenMinutes` | 60, 15 | Access token lifetimes |
| `Otp:FixedCode` | `123456` in Development | Every sign-in code is this value. Ignored in Production |
| `Maintenance:Enabled`, `Maintenance:IntervalSeconds` | `true`, 5 | The background job that expires offers and raises waiting-pickup alerts |
| `Maintenance:StaleCollectorMinutes` | 30 (0, off, in Development) | Take collectors offline when their app stops reporting |
| `Storage:Root` | `storage/` beside the app | Where uploaded photos are kept (local disk for now) |
| `Storage:PublicBaseUrl` | the request's host | Base of catalogue photo URLs, such as `https://api.rethread.lk` |
| `Portal:BaseUrl` | `http://localhost:3000` | Where staff invite links point (`/set-password?token=…`) |
| `Simulation:AutoAnswerOffers` | `false` | Development only: answer call-centre offers like the portal's mock collectors, to demo the portal without a phone |

## Auth

| Audience | Sign-in | Token role |
| --- | --- | --- |
| User app | `POST /api/v1/auth/otp/request` then `/otp/verify` (mobile or email) | `customer` |
| Collector app | `POST /api/v1/collector/auth/otp/request` then `/otp/verify` (collector code and mobile) | `collector` |
| Portal | `POST /api/v1/admin/auth/login` (email and password) | `admin` or `callCentre` |

Access tokens are JWTs; refresh tokens are random, stored hashed and rotated on every use. A refresh token
used twice ends every session of that account. Suspension and staff roles are checked against the database
on every request, not taken from the token. Until an SMS provider is chosen, codes and invite links are
written to the log.

Set secrets with `dotnet user-secrets` locally and environment variables elsewhere
(`ConnectionStrings__Default`, for example).

## Status

| Step | What | State |
| --- | --- | --- |
| 1 | Project, shared plumbing, test fixture, CI | Done |
| 2 | Data model and dev seed | Done |
| 3 | Auth for customers, collectors and staff | Done |
| 4 | Customer profile, addresses, family, bank details | Done |
| 5 | Customer pickups | Done |
| 6 | Collector jobs | Done |
| 7 | Load, hubs and earnings | Done |
| 8 | Call-centre assignments | Done |
| 9 | Wallet and redemptions | Done |
| 10 | Shop, orders and catalogue | Done |
| 11 | Admin operations | Done |
| 12 | Rates and impact | Done |
| 13 | Deploy readiness | Planned |
