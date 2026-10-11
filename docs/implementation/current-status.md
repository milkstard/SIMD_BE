# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/04_02_incident-persistence-and-migration.md` (BE-04, part 2 of 2) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-11 |
| Last updated | 2026-10-11 |
| Branch / commit | main / uncommitted |

### Progress
- [x] Application: `ConflictException` unsealed, `DuplicateIncidentException`, `IAppDbContext` DbSets
- [x] Infrastructure: `Sequences`, `IncidentConfiguration`, `IncidentHistoryConfiguration`, `AppDbContext` (fingerprint mapping, append-only guard)
- [x] Migration `AddIncidents` (sequence, tables, indexes, full-text catalog + index, `Down`)
- [x] Ops: `docker/mssql-fts/Dockerfile`, docker-compose `mssql` service, `ApiFactory` builds the image and uses its own database
- [x] Tests: `IncidentPersistenceTests` (13 integration tests), `InvokeAsync_DuplicateIncident_Returns409` (unit)
- [x] Docs: spec 04_02 open questions resolved, DATABASE.md, CLAUDE.md commands

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test` (all, Docker up): Domain 290, Application 53, Api.UnitTests 42, Api.IntegrationTests 140 passed, 0 failed (13 are the new `IncidentPersistenceTests`).
- `dotnet format --verify-no-changes`: clean (exit 0).
- `docker compose build mssql` succeeded (first build ~3 min); `docker compose up -d --wait mssql` healthy, `SERVERPROPERTY('IsFullTextInstalled')` = 1.
- Found by the first integration run and fixed: the factory migrated into `master`, where SQL Server refuses full-text indexes, so `ApiFactory` now uses its own database `incidenthub_tests`. Two test-only bugs fixed (index query with two ordered aggregates; history ordering tie on equal timestamps).

### Blockers / open questions
None.

### Review
`/review-spec` on 2026-10-11 · change set commit `c0d9f45` (04_02 part).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 525 passed / 0 failed / 0 not run; 11/11 AC met; 3 Medium + 5 Low; proposed tests 3 |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 4 Low (plus pre-existing MessagePack 2.5.108 High advisories in `IncidentHub.Api`, not introduced by 04_02) |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 3 Low |

Verdict: **Approved**.

## Log
- 2026-10-11 · 04_02 · Reviewed · Approved — QA 525/525, security 4 Low, code review 1 Medium (migration half-applies if Full-Text is missing).
- 2026-10-11 · 04_02 · Done · Persistence, `AddIncidents` migration (with full-text), FTS image and tests; full suite green with Docker.
- 2026-10-11 · 04_02 · Blocked · Code complete; integration run waited for Docker.
- 2026-10-11 · 04_02 · In progress · Started; nothing is deferred to BE-04.
- 2026-10-11 · 04_01 · Done · Incident aggregate, workflow and error mappings implemented (290 domain, 41 API, 53 application unit tests passing).
