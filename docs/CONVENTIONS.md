# Conventions

## Naming

| Thing | Convention | Example |
| --- | --- | --- |
| Files | One public type per file, named after it | `PickupWorkflow.cs` |
| Endpoints | `<Feature>Endpoints.cs` with a `Map<Feature>` extension | `JobsEndpoints.MapJobs` |
| Requests, responses | `record`s named after the contract | `JobDetail`, `CollectionRequest` |
| Private fields | `_camelCase` | `_clock` |
| JSON | camelCase keys and enum values, from the shared options | `"onTheWay"` |
| Money | `long`, ending in `Cents` | `PayoutCents` |
| Weight | `decimal`, ending in `Kg` | `WeightKg` |
| Error codes | `UPPER_SNAKE_CASE` constants next to the feature | `JOB_TAKEN` |

## Code

- File-scoped namespaces, `var` where the type is obvious, and nullable on everywhere.
- No `DateTime.Now` or `DateTime.UtcNow`: use `TimeProvider`.
- Async all the way, with the request's `CancellationToken`.
- Comments explain why, not what.
- `dotnet format` and warnings-as-errors keep the build clean. If you suppress a rule, say why.

## Branches and commits

Branch from `main` as `feature/<name>`, `fix/<name>` or `chore/<name>`, and use
[Conventional Commits](https://www.conventionalcommits.org):

```text
feat(jobs): let collectors accept open jobs
fix(wallet): refund rejected transfers
chore: update packages
```

## Pull request checklist

- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` pass
- [ ] New endpoints match their contract sample (`MatchesContract`) and cover every documented error code
- [ ] New endpoints are in the right route group and policy
- [ ] Migrations are added for model changes and named for what they do
