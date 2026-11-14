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
`rethread` database. In Development the API applies migrations on start. It listens on
http://localhost:5080, with the API reference at http://localhost:5080/docs and health at `/health`.

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

Set secrets with `dotnet user-secrets` locally and environment variables elsewhere
(`ConnectionStrings__Default`, for example).

## Status

| Step | What | State |
| --- | --- | --- |
| 1 | Project, shared plumbing, test fixture, CI | Done |
| 2 | Data model and dev seed | Planned |
| 3 | Auth for customers, collectors and staff | Planned |
| 4 | Customer profile, addresses, family, bank details | Planned |
| 5 | Customer pickups | Planned |
| 6 | Collector jobs | Planned |
| 7 | Load, hubs and earnings | Planned |
| 8 | Call-centre assignments | Planned |
| 9 | Wallet and redemptions | Planned |
| 10 | Shop, orders and catalogue | Planned |
| 11 | Admin operations | Planned |
| 12 | Rates and impact | Planned |
| 13 | Deploy readiness | Planned |
