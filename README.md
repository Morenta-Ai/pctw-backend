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

Every client's e2e or contract checks have been run against this API: the portal's Playwright suite
passes against it (`E2E_API_BASE_URL`), and the apps' contracts are checked sample by sample in
`dotnet test`.

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
| `Maintenance:StaleCollectorMinutes` | 30 (0, off, in Development and Staging, where the demo collectors never report) | Take collectors offline when their app stops reporting |
| `Storage:PublicBaseUrl` | the request's host | Base of catalogue photo URLs, such as `https://api.rethread.lk` |
| `Pickups:SlotCapacity` | 40 | Pickup requests per slot before it shows as unavailable |
| `ServiceArea:MinLatitude` … `MaxLongitude` | Sri Lanka | Where customers can save an address |
| `App:*` | see `AppConfigEndpoints` | What `GET /app/config` tells the user app: support contacts, legal and store links, `WalletEnabled`, `PushEnabled`, `MinSupportedVersion`, `LatestVersion` |
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

## Deploying

The API is a single container: `docker build -t rethread-api .` gives an image that listens on 8080 and
runs as a non-root user. Production needs:

| Variable | Example |
| --- | --- |
| `ConnectionStrings__Default` | `Host=ep-…-pooler.….aws.neon.tech;Database=neondb;Username=…;Password=…;SSL Mode=VerifyFull;Channel Binding=Require` |
| `Jwt__SigningKey` | 32+ random characters, from the platform's secret store |
| `Storage__PublicBaseUrl` | `https://api.rethread.lk` (catalogue photo links) |
| `Portal__BaseUrl` | `https://portal.rethread.lk` (staff invite links) |
| `Hosting__TrustForwardedHeaders` | `true` behind a load balancer or platform proxy |

Run migrations as a deploy step, not on start: `dotnet Rethread.Api.dll migrate` (in the container,
`docker run … rethread-api migrate`). Use Neon's direct (non-pooled) connection string for that, and the
pooled one for the API. The seed and the development switches (`Otp:FixedCode`,
`Simulation:AutoAnswerOffers`) never run in Production.

Neon gives a `postgresql://…` URL; Npgsql needs it as the keyword string above. The direct host is the
pooled one without `-pooler`. To load the demo data into Neon, or start the hosted demo again dated
today (before a presentation, say), run `seed --reset` with `ASPNETCORE_ENVIRONMENT=Development` and the
direct connection string. It clears the `public` schema, keeping the database, so everything entered since
is gone and everyone signs in again. Keep the strings in a gitignored `.env`, never in `appsettings*.json`.

Before going live:

- Choose an SMS provider and an email sender: codes and invite links only go to the log today.
- Uploads are kept in Postgres (`file_contents`), which suits photos of a few MB. Move them to object
  storage (S3, R2, Azure Blob or Vercel Blob) behind `IFileStore` if they grow.
- Protect the Data Protection keys (they encrypt bank account numbers) with a certificate or a key vault.
- Choose a card payment gateway and implement `IPaymentGateway`: until then Production confirms no card
  payment, so shop orders can't be paid. Development accepts the user app's mock gateway.

### Vercel

The test deployment is https://pctw-backend.vercel.app. Vercel builds `Dockerfile.vercel` as a container
function, so the project's framework preset must be Container. Set these in the project's environment
variables:

| Variable | Value |
| --- | --- |
| `PORT` | `8080`: Vercel sends traffic to this port, and the image's non-root user can't listen on 80 |
| `ASPNETCORE_ENVIRONMENT` | `Staging`: sign-in codes are `Otp__FixedCode` and the apps' mock card payments are accepted, because there's no SMS provider or gateway yet. `Production` once there is |
| `Otp__FixedCode` | `123456` (Staging only; Production ignores it) |
| `ConnectionStrings__Default` | Neon's pooled connection string |
| `Jwt__SigningKey` | `openssl rand -base64 48` |
| `Storage__PublicBaseUrl` | `https://pctw-backend.vercel.app` |
| `Portal__BaseUrl` | `https://pctw-admin.vercel.app` |

Things that work differently on Vercel:

- Instances stop after 5 idle minutes, so the background maintenance only runs while there's traffic.
  The rules that matter (offer expiry, waiting alerts) also run on the reads that depend on them.
- Request bodies are capped at 4.5 MB. The portal shrinks product photos before uploading them.
- Vercel doesn't run migrations. Run `migrate` with Neon's direct connection string before deploying a
  change that adds one.

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
| 13 | Deploy readiness | Done |
| 14 | User app's current contract: auth, profile, addresses, family, catalog | Done |
| 15 | User app: pickups with preferred dates, confirmation and tracking | Done |
| 16 | User app: impact summary, history, factors and milestones | Done |
| 17 | User app: shop, quote, card checkout and orders | Done |
| 18 | User app: inbox, devices, support, app config and home | Done |
