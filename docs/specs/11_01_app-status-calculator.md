# Spec 11_01: Application status calculator, cache and `GET /dashboard/app-status`

> Status: **Done** · Source: BE-11 in docs/BACKEND-SPECS.md · Part 1 of 3

## 1. Purpose & scope

Derive the status of every application (`Operational` / `Degraded` / `Outage`) from its open incidents, never from user input, and expose it.

In scope:
- `AppStatusCalculator` (pure, Domain), pulled in from deferred ("`AppStatusCalculator`", 04_01 → BE-11).
- `GET /api/v1/dashboard/app-status`.
- Redis cache of the derived status per application, invalidated on every relevant incident change.
- `AppStatusChanged` broadcast to `dashboard:{teamId}` when the derived status changes, pulled in from deferred (09_02 → BE-11).

## 2. Dependencies

BE-04 (`Incident`, `Severity`, `IncidentStatus`), BE-07 (list visibility rules, `IAppTeamReader`), BE-09 (post-commit hook, `IncidentBroadcaster`, `IIncidentClient.AppStatusChanged`).
`AppStatus`, `AppStatusItemDto` and the `AppStatusChanged` client method already exist (contract shape only).

## 3. Design

### Domain
`Apps/AppStatusCalculator` (static or sealed, no dependencies):
`Calculate(IEnumerable<OpenIncidentFact>)` → `AppStatusResult(Status, OpenIncidents, WorstOpenSeverity, Since)`.
- Rule: an open incident with `Severity = Critical` and `Environment = Production` → `Outage`; else any open `High` or `Critical` → `Degraded`; else `Operational`.
- `OpenIncidentFact(Severity, Environment, CreatedAt)`; "open" is decided by the caller's query (OQ 1).
- `Since`: earliest `CreatedAt` among the open incidents that cause the current status; `null` for `Operational` (OQ 6).
- `WorstOpenSeverity`: most severe open incident, `null` when none.

### Application (`Dashboard/`)
- `Abstractions/IAppStatusReader`: `GetAsync(IReadOnlyCollection<Guid> applicationIds, ct)` → `IReadOnlyList<AppStatusItemDto>` and `GetAsync(Guid applicationId, ct)`. Returns the cached value, else computes it.
- `GetAppStatus/GetAppStatusQuery` + handler + (no validator needed, no input): resolves the caller's visible applications through `IAppTeamReader.GetApplicationIdsForUserAsync` (Admin included, as in 07_01), reads statuses, returns `AppStatusItemDto[]` ordered by application name.
- `OpenIncidentFacts` query helper (shared with 11_02): the one `AsNoTracking()` projection that selects open incidents grouped by application. One definition of "open" for the whole dashboard.
- `Realtime/IncidentBroadcaster`: for every touched incident's application, compare the freshly derived status with the cached one; when different (or none cached), add an `AppStatusChangedMessage(AppStatusItemDto, [Dashboard(teamId)])` to the plan.

### Infrastructure
- `Caching/RedisAppStatusReader : IAppStatusReader`: key `appstatus:{applicationId:N}`, value JSON of `AppStatusItemDto`. Redis first, SQL on a miss, repopulates the cache. No TTL-only reliance: the key is also deleted on invalidation. Safety TTL 60 s so a missed invalidation heals (Worker changes, OQ 5). Span on `InfrastructureTelemetry.Source` (`appstatus.get`, tags `cache.hit`, `cache.miss`).
- Invalidation: `IAppStatusReader.InvalidateAsync(applicationId)` is called from the post-commit path for every `IncidentReported`, `IncidentTransitioned`, `IncidentUpdated` (severity, environment or application change: both the old and the new application) and `IncidentAssigned` is not relevant. Invalidation failure is logged, never rethrown (best effort, like the broadcast).
- Nothing changes in the schema. No migration.

### Api
`Controllers/DashboardController` (`[Authorize]`, thin): `GET dashboard/app-status` → `ISender.Send(new GetAppStatusQuery())`. Any signed-in role.

### Worker
Nothing. Worker-side changes do not invalidate until BE-12 (the 60 s safety TTL bounds the staleness).

## 4. Endpoints & DTOs

| Method | Path | Success |
|---|---|---|
| GET | `/dashboard/app-status` | `200` `AppStatusItem[]` |

`AppStatusItem` is unchanged from API-CONTRACT §5. Update API-CONTRACT: the endpoint notes (visibility, ordering, only active applications, the "derived, never set" rule) and the `AppStatusChanged` recipient note in §7. Regenerate `contracts/swagger.json` (part 11_03 owns the final regeneration, but run `scripts/generate-openapi.ps1` here so the diff is reviewable).

## 5. Acceptance criteria

- [ ] `AppStatusCalculator`: open Critical in Production → `Outage`; open High (any environment) → `Degraded`; open Critical outside Production → `Degraded`; only Medium/Low or nothing open → `Operational`.
- [ ] Resolved and Closed incidents never count (OQ 1); a `Reopened` incident does.
- [ ] Status is never settable: no request DTO, command or endpoint carries an `AppStatus`; there is no `AppStatus` column (architecture test over DTOs and the model).
- [ ] `GET /dashboard/app-status` returns one item per **active** application visible to the caller, scoped to their teams (Admin included); an application of another team is absent.
- [ ] Reporting a Critical Production incident turns the application `Outage` on the next read (cache invalidated); resolving it returns `Operational`.
- [ ] A Redis miss falls back to SQL and repopulates the key.
- [ ] A severity or environment edit that moves an incident out of or into the status rules updates the status.
- [ ] `AppStatusChanged` is sent to `dashboard:{teamId}` only when the status changed, and not when an incident save leaves it the same (hub-level test).
- [ ] `AppStatusChanged` reaches only the owning team's dashboard group.
- [ ] Redis down: the endpoint still answers from SQL (cache errors are logged, not surfaced).
- [ ] `dotnet build` (warnings as errors) and `dotnet format` clean; `API-CONTRACT.md` updated.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Apps/AppStatusCalculatorTests`:
`Calculate_CriticalInProduction_ReturnsOutage`, `Calculate_CriticalInUat_ReturnsDegraded`, `Calculate_HighOnly_ReturnsDegraded`, `Calculate_MediumAndLow_ReturnsOperational`, `Calculate_NoOpenIncidents_ReturnsOperationalWithNoSince`, `Calculate_TwoCauses_SinceIsEarliest`, `Calculate_WorstSeverity_IsMostSevereOpen`.
`tests/IncidentHub.Application.UnitTests/Dashboard/`: `GetAppStatusHandler_OtherTeamApplication_IsNotReturned`, `GetAppStatusHandler_InactiveApplication_IsNotReturned`, `IncidentBroadcaster_StatusChanged_PlansAppStatusMessage`, `IncidentBroadcaster_StatusUnchanged_PlansNoAppStatusMessage` (NSubstitute fakes).
`tests/IncidentHub.Api.UnitTests/`: `AppStatus_IsNotInAnyRequestDto` (reflection over request records and controllers), `DashboardController_RequiresAuthorization`.
`tests/IncidentHub.Api.IntegrationTests/Dashboard/AppStatusTests` — `[Trait("Category","Integration")]`, `ApiFactory`:
`Get_CriticalProductionReported_ReturnsOutage`, `Get_AfterResolve_ReturnsOperational`, `Get_SeverityEdited_StatusFollows`, `Get_CacheKeyDeleted_FallsBackToSqlAndRepopulates`, `Get_OtherTeamApp_Absent`, `Get_Unauthenticated_Returns401`, `Get_RedisUnavailable_StillAnswers`.
`tests/IncidentHub.Api.IntegrationTests/Hubs/AppStatusBroadcastTests`: `Report_CriticalProduction_DashboardClientReceivesAppStatusChanged`, `Update_StatusUnchanged_NoAppStatusChanged`, `Report_OtherTeamClient_ReceivesNothing`.

## 7. Out of scope

- The summary endpoint and `DashboardChanged` payload → 11_02.
- `GET /me` and OpenAPI determinism → 11_03.
- Re-evaluating status when the Worker changes an incident (SLA breach) → BE-12 (deferred item added, needs a cross-process invalidation path).
- Status history, uptime percentages, manual overrides (status stays derived-only).

## 8. Open questions

1. **"Open" is undefined** in CLAUDE.md and BACKEND-SPECS. Recommendation: every status except `Resolved` and `Closed` (`Reopened` counts). The same definition applies to 11_02.
2. **Environment rule.** BACKEND-SPECS says "open High → Degraded" (any environment) and "open Critical in Production → Outage". An open Critical outside Production is not covered literally. Recommendation: `Degraded` (it is at least as bad as High).
3. **Visibility.** Scope the list to the caller's teams (as `GET /incidents`, 07_01) or show every application to everyone? Recommendation: team-scoped, Admin included.
4. **Inactive applications.** Recommendation: omitted from the list.
5. **Cache across processes.** Invalidation runs in the API's post-commit hook only; the Worker is not covered until BE-12. The 60 s safety TTL (vs the 5 s the summary uses) is a proposal.
6. **`Since`.** No stored timestamp exists for "status started". Recommendation: derive from the earliest `CreatedAt` of the causing open incidents (can overstate when a longer-lived cause was resolved and another remains), with no new column.
7. **Ordering.** Recommendation: by application name, as `GET /applications`.
8. **Doc conflict.** API-CONTRACT §7 says `AppStatusChanged` goes to `dashboard:{teamId}` only. Confirm that it should not reach `team:{teamId}`.

## 9. Decisions (2026-10-14)

1. Open questions 1-8 accepted as recommended (open = not Resolved/Closed; Critical outside Production = Degraded; team-scoped incl. Admin; inactive omitted; 60 s safety TTL; `Since` from the earliest causing incident; ordered by name; `AppStatusChanged` to `dashboard:{teamId}` only).
2. `IncidentUpdated` gains an optional `PreviousApplicationId` (set only when `ApplicationId` changed) so the post-commit path can refresh the application the incident left. Additive in the outbox payload.
3. `IAppStatusReader.InvalidateAsync` became `RefreshAsync`: it reads the cached status, recomputes from SQL and overwrites the key, returning the previous and current status. One call replaces invalidate-then-compare and avoids a reader repopulating the old value in between. The single-id `GetAsync` overload was not added (nothing calls it yet).
4. The cached entry stores the owning team id next to the item; names in the endpoint response come from SQL, not the cache.
5. `GetAppStatusHandler` tests that need the database (other team, inactive application) are integration tests, since `DbContext` is not mocked.
6. Review follow-ups (2026-10-14): a failing status refresh is logged and skipped so the other broadcasts of the save are still sent (cancellation still propagates); the miss-path cache write uses `When.NotExists` so it never overwrites a fresher refreshed value; span tags are `cache.hit`/`cache.miss`. With nothing cached (TTL expired) the first derivation is pushed even if unchanged, so a recovery is never missed; clients treat `AppStatusChanged` as idempotent.
7. Known limit: an authenticated request needs Redis today because the sign-in sync (`UserDirectory.EnsureUserAsync`) calls it, so `GET /dashboard/app-status` cannot answer during a Redis outage; the reader itself degrades to SQL (tested at reader level). Recorded in `deferred-items.md`.
