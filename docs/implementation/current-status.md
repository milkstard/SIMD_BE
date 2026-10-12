# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/05_01_sla-clock-and-outbox.md` (BE-05, part 1 of 2) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-12 |
| Last updated | 2026-10-12 |
| Branch / commit | main / uncommitted |

### Progress
- [x] Domain: `SlaPolicy`, `SlaClock`, `SlaDueTimes`
- [x] Application: `Microsoft.Extensions.Options` package, `ISlaPolicyProvider`, `SlaDefaultsOptions`, `SlaDefaultsOptionsValidator`
- [x] Infrastructure: `OutboxMessage`, `OutboxJson`, `OutboxMessageConfiguration`, `AppDbContext` outbox staging, `ConfiguredSlaPolicyProvider`, DI
- [x] Migration `AddOutboxMessages`
- [x] Config: `Sla:Defaults` in Api and Worker appsettings
- [x] Tests: Domain, Application, Api unit, `OutboxPersistenceTests` (integration)
- [x] Docs: DATABASE.md index, deferred/completed items, spec status

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test` (all, Docker up): Domain 306, Application 59, Api.UnitTests 46, Api.IntegrationTests 150 passed, 0 failed (10 are the new `OutboxPersistenceTests`; 25 new unit tests).
- `dotnet format --verify-no-changes`: clean (the generated migration was formatted with `dotnet format`).
- `docker compose build mssql` cached; integration run used Testcontainers SQL Server (FTS image) + Redis.

### Blockers / open questions
None. OQ 3 (outbox index `INCLUDE (OccurredAt)`) decided: add it.

### Review
`/review-spec` on 2026-10-12 · change set: uncommitted on top of `72b76d6` (05_01).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 561 passed / 0 failed / 0 not run; 9/9 AC met; 0 Critical/High/Medium, 2 Low; proposed tests 0 |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low (plus pre-existing MessagePack 2.5.108 and test-only SSH.NET advisories, not introduced by 05_01) |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 4 Low |

Verdict: **Approved**.

Follow-up (2026-10-12, after review): code review M1, L1, L2, L4 fixed; L3 kept by decision (OQ 5). Rebuilt, `dotnet format` clean, full suite re-run: Domain 306, Application 64, Api.UnitTests 49, Api.IntegrationTests 152 passed, 0 failed.

## Log
- 2026-10-12 · 05_01 · Fixed · Code review follow-ups: shared sync/async save logic (sync path now translates unique violations), shared escalate-percent constants, provider uses `IOptions`, event-name tests; 571 tests green.
- 2026-10-12 · 05_01 · Reviewed · Approved — QA 561/561, security 2 Low, code review 1 Medium (sync `SaveChanges` duplicates the async path and does not translate unique violations).
- 2026-10-12 · 05_01 · Done · SlaClock, config-backed SLA defaults, outbox table and same-save event staging, `AddOutboxMessages` migration; full suite green with Docker.
- 2026-10-12 · 05_01 · In progress · Started; pulls in SlaClock/default SLA and OutboxMessage/events-to-outbox from deferred items.
- 2026-10-11 · 04_02 · Reviewed · Approved — QA 525/525, security 4 Low, code review 1 Medium (migration half-applies if Full-Text is missing).
- 2026-10-11 · 04_02 · Done · Persistence, `AddIncidents` migration (with full-text), FTS image and tests; full suite green with Docker.
- 2026-10-11 · 04_02 · Blocked · Code complete; integration run waited for Docker.
- 2026-10-11 · 04_02 · In progress · Started; nothing is deferred to BE-04.
- 2026-10-11 · 04_01 · Done · Incident aggregate, workflow and error mappings implemented (290 domain, 41 API, 53 application unit tests passing).
