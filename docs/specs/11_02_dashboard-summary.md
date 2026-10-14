# Spec 11_02: Dashboard summary (`GET /dashboard/summary`)

> Status: **Done** · Source: BE-11 in docs/BACKEND-SPECS.md · Part 2 of 3

## 1. Purpose & scope

Return the counts and KPIs behind the dashboard, Redis first with a SQL fallback and a 5 s TTL, and feed the same data to `DashboardChanged`.

In scope:
- `GET /api/v1/dashboard/summary?range=24h|7d|30d` (default `7d`).
- Production `IDashboardSummaryReader`, replacing `NullDashboardSummaryReader`, so `DashboardChanged` carries a real payload (pulled in from deferred: "`DashboardChanged` payload", 09_02 → BE-11).
- Open counts by severity and by application, unassigned, SLA breached, MTTA, MTTR, daily trend.

## 2. Dependencies

11_01 (shared "open incident" definition and the post-commit hook), BE-04, BE-07 (visibility), BE-09 (`DashboardThrottle`, `HubDashboardBroadcaster`).
`DashboardSummaryDto`, `OpenByApplicationDto`, `TrendPointDto` already exist (contract shape only).
`IncidentDailyStats` does not exist until BE-15, so everything is computed from `Incidents` here.

## 3. Design

### Application (`Dashboard/`)
- `GetDashboardSummary/GetDashboardSummaryQuery(string Range)` + `Handler` + `Validator` (`range` must be `24h`, `7d` or `30d`; missing means `7d`; anything else `422` on `range`).
- `Abstractions/IDashboardSummaryProvider`: `GetAsync(IReadOnlyCollection<Guid> teamIds, DashboardRange range, ct)` → `DashboardSummaryDto`. The handler resolves the caller's team set (Admin included, OQ 3) and passes it.
- `DashboardRange` value (`H24`, `D7`, `D30`) with its window start computed from `TimeProvider`.
- `DashboardSummaryQueries`: `AsNoTracking()` projections straight to numbers, never loaded aggregates:
  - `openTotal`, `unassigned` (`AssigneeId IS NULL`), `slaBreached` (`IsSlaBreached`) over open incidents of the team set, not range-limited (current state).
  - `openBySeverity`: all four severities present, zero filled.
  - `openByApplication`: `{ application, open, critical }`, ordered by `open` desc then name; only applications with at least one open incident (OQ 5).
  - `mttaMinutes`: mean of `AcknowledgedAt - CreatedAt` for incidents acknowledged within the range; `mttrMinutes`: mean of `ResolvedAt - CreatedAt` for incidents resolved within the range; `null` when none.
  - `trend`: one point per day of the range (UTC, `YYYY-MM-DD`), `reported` = created that day, `resolved` = `ResolvedAt` that day; days without data are zero-filled. Server computes in UTC (OQ 6).
- `Realtime/DashboardSummaryReader : IDashboardSummaryReader` (replaces the null object): for `DashboardChanged`, loads the summary of the team set `{teamId}` for the default range `7d`, through the same provider (cache included).

### Infrastructure
- `Caching/RedisDashboardSummaryProvider`: key `dashboard:summary:{range}:{hash of sorted team ids}`; Redis first, SQL on a miss, repopulates with a **5 s TTL**; `GeneratedAt` is the time of computation so clients see the cache age. Cache errors are logged and fall back to SQL. Concurrent misses are collapsed with a short `SET NX` lock so a burst does not run the query many times (stampede guard).
- Span `dashboard.summary` on `InfrastructureTelemetry.Source`, tags `cache.hit`, `range`.
- Check the query plans against `IX_Incidents_Status_Severity` and `(ApplicationId, CreatedAt)`. Add no index unless the plan test shows a scan on a large table; if one is needed it comes with its own migration and a `DATABASE.md` update.
- Registration replaces the null object in the API host only; the Worker keeps the default (nothing broadcasts there).

### Api
`DashboardController.GetSummary([FromQuery] string? range)` → `ISender.Send` → `200`. Any signed-in role.

## 4. Endpoints & DTOs

| Method | Path | Success |
|---|---|---|
| GET | `/dashboard/summary?range=7d` | `200` `DashboardSummary` |

`DashboardSummary` is unchanged. Update API-CONTRACT: `range` default, `422` on an invalid range, visibility (caller's teams), definitions of `openTotal`/MTTA/MTTR/trend, the cache age note, and remove the "not sent until the dashboard summary exists (BE-11)" remark in §7. Regenerate `contracts/swagger.json`.

## 5. Acceptance criteria

- [ ] The summary counts only open incidents of applications owned by the caller's teams; data of another team never appears in any count (Admin included).
- [ ] `openBySeverity` contains all four severities; `openByApplication` agrees with the sum of `openTotal`.
- [ ] `mttaMinutes` and `mttrMinutes` follow the definitions above and are `null` when no incident qualifies; values are rounded to 1 decimal.
- [ ] `trend` has exactly one point per day of the range, zero filled, ordered ascending.
- [ ] A Redis miss falls back to SQL and repopulates the cache; a second call inside 5 s does not touch SQL.
- [ ] After the 5 s TTL the next call recomputes (tested with a fake `TimeProvider` or key TTL assertion).
- [ ] An invalid `range` is `422` on `range`; a missing one means `7d`.
- [ ] Redis unavailable: the endpoint answers from SQL.
- [ ] `DashboardChanged` now carries a populated `DashboardSummary` for the owning team and is still capped at one per second per team and instance.
- [ ] The query on the seeded 50k-row set stays index-backed (opt-in plan test) and its p95 is recorded in the spec's verification.
- [ ] `dotnet build`, `dotnet format` clean; `API-CONTRACT.md` and `swagger.json` updated.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Dashboard/`: `Validator_InvalidRange_Fails`, `Validator_MissingRange_UsesDefault`, `Handler_ResolvesCallerTeams_PassesThemToProvider`, `DashboardRange_WindowStart_UsesTimeProvider`, `DashboardSummaryReader_ProvidesSummaryForTeam`.
`tests/IncidentHub.Api.IntegrationTests/Dashboard/DashboardSummaryTests` — `[Trait("Category","Integration")]`:
`Get_SeededIncidents_CountsMatch`, `Get_OtherTeamIncidents_NotCounted`, `Get_AllSeveritiesPresent_WhenNoneOpen`, `Get_MttaMttr_ComputedFromTimestamps`, `Get_NoAcknowledged_MttaIsNull`, `Get_Trend_ZeroFilledPerDay`, `Get_InvalidRange_Returns422`, `Get_DefaultRange_Is7d`, `Get_CacheMiss_RepopulatesWithFiveSecondTtl`, `Get_WithinTtl_DoesNotQuerySql`, `Get_RedisUnavailable_FallsBackToSql`, `Get_Unauthenticated_Returns401`.
`tests/IncidentHub.Api.IntegrationTests/Hubs/BroadcastTests` (extend): `Report_Committed_DashboardChangedCarriesSummary`.
Opt-in plan test (`Category=Performance`): `DashboardSummaryQueryPlanTests` on the 50k-row seed, asserts no clustered scan on the open-incident count.

## 7. Out of scope

- Reading `IncidentDailyStats` for trend and MTTA/MTTR → BE-15 (deferred item added).
- Refreshing the cache and the broadcast when the Worker changes incidents (SLA breach) → BE-12.
- `GET /reports/incidents.csv` → BE-16.
- Per-application or per-environment filters on the summary, custom date ranges, time zones other than UTC.

## 8. Open questions

1. **"Open" is undefined.** Same recommendation as 11_01 OQ 1 (not `Resolved`/`Closed`); `slaBreached` and `unassigned` follow it.
2. **MTTA/MTTR window.** "Within range" is taken as acknowledged/resolved within the range. The alternative (incidents created within the range) gives different numbers. Recommendation: the former.
3. **Visibility.** The contract sketches one global summary; the rest of the API is team-scoped. Recommendation: team-scoped (Admin included), cache key includes the team set. Cost: a cache entry per distinct team set.
4. **`DashboardChanged` range.** The pushed payload uses the default `7d`; clients on `24h` or `30d` refetch. Confirm, or send one event per range.
5. **`openByApplication`** lists only applications with open incidents; an alternative lists all visible applications with zeros. Recommendation: only those with open incidents.
6. **Day boundaries** are UTC; the trend of a user in another time zone shifts at midnight UTC.
7. **Doc conflict.** BACKEND-SPECS says "counts by status" but the contract's `DashboardSummary` has no status breakdown. The contract wins; no status breakdown is added.

## 9. Decisions (2026-10-14)

1. Open questions 1-7 accepted as recommended (open = not Resolved/Closed; MTTA/MTTR over incidents acknowledged/resolved in the window; team-scoped incl. Admin; the push carries `7d`; `openByApplication` only lists applications with open incidents; UTC days; no status breakdown).
2. **Window:** whole UTC days. The window starts at UTC midnight (N-1) days ago and runs to now, with N = 1/7/30 for `24h`/`7d`/`30d`. MTTA/MTTR and the trend share it, so `24h` is today only (one trend point) and the trend has exactly N points.
3. **Push freshness:** `DashboardSummaryReader` calls `IDashboardSummaryProvider.RefreshAsync` (compute from SQL, overwrite the cache entry) instead of a cached read, because the push runs right after a commit and a cache entry up to 5 s old would carry the state from before the change. The throttle bounds this to one computation per team per second.
4. MTTA/MTTR are averaged in memory over two timestamp columns per qualifying incident, not with `DATEDIFF`: provider functions stay out of Application (ADR 0001).
5. Visibility counts every application owned by the caller's teams, inactive ones included, like `GET /incidents`.
6. An empty `range=` binds to null and means the default `7d`; only a non-empty unknown value is `422`.
7. The cache stores the DTO with default (PascalCase) System.Text.Json names; the key is `dashboard:summary:{range}:{hash of the sorted team ids}`. A request that loses the `SET NX` guard polls up to 500 ms for the winner's entry, then computes itself; a Redis failure skips the wait.
8. Known effect: every API-side incident save refreshes the team's `7d` entry through the post-commit hook, so the cache is usually fresher than 5 s; tests that seed several incidents must drop the entry before asserting.

9. Review follow-ups (2026-10-14): the miss path writes the cache with `When.NotExists` so it cannot overwrite a fresher post-commit entry (SEC-1); `TryParse` falls back to `7d` on failure (code review L3); API-CONTRACT section 7 says that `DashboardChanged` is one team's summary and that a multi-team client refetches (code review M1).

## 10. Verification

- Opt-in plan test (`DashboardSummaryQueryPlanTests`, 50k rows): the open grouping is `Index Seek on IX_Incidents_Status_Severity` + `Clustered Index Seek on PK_Incidents`, no clustered scan; p95 of the uncached 30d summary 197.9 ms.
