# Spec 12_01: SLA policies table and admin API

> Status: **Draft** · Source: BE-12 in docs/BACKEND-SPECS.md · Part 1 of 4

## 1. Purpose & scope

Replaces the config-backed SLA defaults with a `SlaPolicies` table (one row per severity) and gives Admins `GET/PUT /api/v1/sla-policies`.

Pulled in from `deferred-items.md`:
- "`SlaPolicies` table" (04_02, → BE-12).
- "Replace config-backed `ISlaPolicyProvider` (`Sla:Defaults`) with the `SlaPolicies` table" (05_01, → BE-12).

Policy changes apply only to incidents created or re-severitied afterwards: `AckDueAt`/`ResolveDueAt` are stored on the incident, and nothing in this part rewrites them.

## 2. Dependencies

BE-02 (Admin policy), BE-04/05 (`SlaClock`, `ISlaPolicyProvider`, `Sla:Defaults`), BE-03 (Teams/Applications endpoint style: `PUT`, `If-Match`, ProblemDetails).

## 3. Design

### Domain
`SlaPolicy` (record, exists) stays the value used by `SlaClock`. The table row is the Domain entity `SlaPolicySetting` (section 9, item 1) with `ToPolicy()` returning the `SlaPolicy`.

### Application
- `Sla/ListSlaPolicies/` query + handler -> `IReadOnlyList<SlaPolicyDto>` ordered Critical, High, Medium, Low.
- `Sla/GetSlaPolicy/` query + handler (by severity id, see OQ 1).
- `Sla/UpdateSlaPolicy/` command + validator + handler: full replace of one row. Validator reuses the `SlaPolicy` invariants (`AckMinutes > 0`, `ResolveMinutes >= AckMinutes`, `EscalateAtPercent` 1-99) plus upper bounds (OQ 4). Uses `IAppDbContext`; `DbUpdateConcurrencyException` maps to 409.
- `ISlaPolicyProvider` is unchanged (callers already use the async method).

### Infrastructure
- Configuration `SlaPolicySettingConfiguration` for the Domain entity `SlaPolicySetting` (section 9, item 1): PK `Severity` stored as string, per `DATABASE.md`; `RowVersion` (OQ 2).
- Migration `AddSlaPolicies`: creates the table and seeds the four rows from the built-in defaults (Critical 15/240/80, High 60/480/80, Medium 240/4320/80, Low 480/7200/80). Seeding in the migration, not at startup (no `Database.Migrate()`).
- `DbSlaPolicyProvider : ISlaPolicyProvider` reads the row with `AsNoTracking()`; registered in `AddSlaPolicies()` instead of `ConfiguredSlaPolicyProvider`. A missing row falls back to `SlaDefaultsOptions` and logs a warning (OQ 3). No cache in v1: the lookup is a 4-row primary-key read, once per create/severity change.
- `Sla:Defaults` stays as the fallback only; its validator keeps running.

### Api
`Controllers/SlaPoliciesController` (`[Authorize(Policy = Policies.RequireAdmin)]`, or the existing Admin policy name): thin, maps to the commands/queries. `rowVersion` is returned as `ETag`; `PUT` requires `If-Match` (`428`/`400`/`409`).

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Notes |
|---|---|---|---|---|
| GET | `/sla-policies` | - | `200` `SlaPolicy[]` | Admin only, always 4 items, ordered by severity rank |
| GET | `/sla-policies/{id}` | - | `200` `SlaPolicy` + `ETag` | `404` unknown |
| PUT | `/sla-policies/{id}` | `UpdateSlaPolicyRequest` | `200` `SlaPolicy` + new `ETag` | `If-Match` required; `422` validation; `403` non-Admin |

`SlaPolicy` in API-CONTRACT has `id` and no `rowVersion`; both change (OQ 1, 2). `UpdateSlaPolicyRequest { ackMinutes, resolveMinutes, escalateAtPercent }` (the severity is the key and cannot change). `API-CONTRACT.md` is updated in the same change (rows, DTO, notes "applies to new and re-severitied incidents only") and `contracts/swagger.json` is regenerated with `scripts/generate-openapi.ps1`.

## 5. Acceptance criteria

- [ ] After the migration the table has exactly four rows equal to the former defaults; creating an incident yields the same due times as before.
- [ ] `ISlaPolicyProvider` reads the table: a `PUT` changes the due times of incidents reported afterwards.
- [ ] Changing a policy does not change `AckDueAt`/`ResolveDueAt` of any existing incident (integration test).
- [ ] `GET`/`PUT` are Admin only (`403` for Reporter/Responder/TeamLead, `401` unauthenticated).
- [ ] `PUT` validates (`ackMinutes > 0`, `resolveMinutes >= ackMinutes`, `escalateAtPercent` 1-99) with `422` and camelCase keys; stale `If-Match` is `409`, missing `428`.
- [ ] A missing table row falls back to `Sla:Defaults` with a logged warning instead of failing the report.
- [ ] `DATABASE.md`, `API-CONTRACT.md`, `swagger.json` updated; build 0 warnings, `dotnet format` clean.

## 6. Test plan

- `Domain.UnitTests`: nothing new (`SlaPolicy`/`SlaClock` covered).
- `Application.UnitTests/Sla/`: `UpdateSlaPolicyValidator_*` (each bound, resolve below ack, percent 0/100), `Handler_UpdatesRow`, `Handler_StaleRowVersion_Throws409Mapped`.
- `Api.UnitTests`/Infrastructure: `DbSlaPolicyProvider_MissingRow_FallsBackToDefaults`.
- `Api.IntegrationTests/Sla/` `[Trait("Category","Integration")]`: `Migration_SeedsFourDefaultRows`, `List_Admin_ReturnsFourOrdered`, `List_Responder_Returns403`, `Put_Valid_UpdatesAndReturnsNewETag`, `Put_StaleIfMatch_Returns409`, `Put_NoIfMatch_Returns428`, `Put_Invalid_Returns422`, `Put_ThenReport_UsesNewMinutes`, `Put_DoesNotChangeExistingIncidentDueTimes`.

## 7. Out of scope

- The monitor, warnings and breach handling -> 12_02/12_03.
- Per-application or business-hours SLAs (minutes stay wall-clock 24x7).
- Policy change history/audit.

## 8. Open questions

1. **`id` of a policy.** API-CONTRACT shows `SlaPolicy.id` and `/sla-policies/{id}`, DATABASE.md makes `Severity` the PK. Proposed: `id` = the severity name (`"Critical"`), no GUID. Confirm; the contract text changes.
2. **Concurrency.** `PUT` elsewhere uses `If-Match`, but `SlaPolicies` has no `RowVersion` in DATABASE.md and its rules say every *mutable aggregate* carries one. Proposed: add `RowVersion` (DATABASE.md updated) for consistency. Alternative: last-write-wins, no `If-Match`.
3. **Missing row.** Fall back to `Sla:Defaults` (proposed) or fail with 500?
4. **Upper bounds.** Proposed `ResolveMinutes` <= 525,600 (one year) and `AckMinutes` <= `ResolveMinutes`, to keep `DateTimeOffset` maths safe.
5. **`PUT /sla-policies` (no id).** BACKEND-SPECS says `GET/PUT /api/v1/sla-policies`; API-CONTRACT lists `/sla-policies/{id}` as well. Proposed: no collection `PUT`, only `PUT /{id}` per severity.

## 9. Decisions (2026-10-19)

1. **Entity in Domain.** The row is `Domain/Incidents/Sla/SlaPolicySetting` (not an Infrastructure `SlaPolicyRow`), exposed as `IAppDbContext.SlaPolicies`: Application handlers cannot reference Infrastructure types. Same shape as `Team`.
2. OQ 1: `id` = the severity name (`"Critical"`), case-insensitive, names only (`"1"` is unknown, `404`). OQ 2: `RowVersion` added, `PUT` requires `If-Match`. OQ 3: a missing row falls back to `Sla:Defaults` with a warning. OQ 4: `ResolveMinutes` <= 525,600 (`ackMinutes <= resolveMinutes` already holds). OQ 5: no collection `PUT`.
3. DB-backed tests (`UpdateSlaPolicyHandlerTests`, `DbSlaPolicyProviderTests`) live in `Api.IntegrationTests`, like the other handler tests that need a database (CLAUDE.md: do not mock `DbContext`); the unit projects get the validator and id-parsing tests.
4. `ConfiguredSlaPolicyProvider` and its unit test are removed; `Sla:Defaults` stays as the seed source of truth for the fallback.
