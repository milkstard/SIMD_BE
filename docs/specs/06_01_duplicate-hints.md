# Spec 06_01: Duplicate hints

> Status: **Implemented** · Source: BE-06 in docs/BACKEND-SPECS.md · Part 01 of 01

## 1. Purpose & scope

Cut down duplicate reports by showing likely matches while the user types a title (System Design F2):
`GET /api/v1/incidents/similar?applicationId=&title=` returns up to 5 **open** incidents (`Status != Closed`) of the
**same application**, ranked by full-text similarity of their `Title` to the typed title.

In scope:
- `FindSimilarIncidents` use case (query + handler + validator).
- `ISimilarIncidentSearch` port in Application, implemented in Infrastructure with `FREETEXTTABLE` over the existing
  full-text index (04_02). Application stays on core EF only (ADR 0001).
- A reusable `IncidentSummaryDto` projection (BE-07's list reuses it).
- `IncidentsController.Similar`.
- 50k-row seed and an opt-in p95 performance test.

Pulled in from `deferred-items.md`: "Full-text 'similar incidents' query using the full-text index" (04_02 → BE-06), which
includes the 50k-row seed for the p95 test. Nothing else is deferred to BE-06.

## 2. Dependencies

- BE-02 (`ICurrentUser`, `Policies.CanReportIncident`, `IAppTeamReader`).
- BE-03 (`MonitoredApp`, `Refs`: `ApplicationRefDto`, `UserRefDto`, `TeamRefDto`).
- BE-04 / 04_02 (`Incident`, full-text catalog `ftIncidentHub` + full-text index on `Title`, `Description` with
  `CHANGE_TRACKING AUTO`, `docker/mssql-fts` image, `ApiFactory` on database `incidenthub_tests`).
- 05_02 (`IncidentSummaryDto`, `IncidentsController`, the "Application not found or not available to you." membership rule).

## 3. Design

### Domain
None.

### Application (`IncidentHub.Application`)
`Abstractions/ISimilarIncidentSearch`:
```csharp
public interface ISimilarIncidentSearch
{
    /// Ids of open incidents of the application whose Title matches the text, best match first.
    Task<IReadOnlyList<Guid>> FindOpenAsync(Guid applicationId, string title, int limit, CancellationToken cancellationToken);
}
```

`Incidents/FindSimilarIncidents/`
- `FindSimilarIncidentsQuery` — sealed record `(Guid ApplicationId, string Title) : IRequest<IReadOnlyList<IncidentSummaryDto>>`.
- `FindSimilarIncidentsValidator` (no data access): `applicationId` not empty; `title` not null/whitespace, length ≤ 200
  (`Incident.TitleMaxLength`).
- `FindSimilarIncidentsHandler(IAppDbContext db, ICurrentUser currentUser, IAppTeamReader teams, ISimilarIncidentSearch search)`:
  1. Load the application (`AsNoTracking`, `Select` of `IsActive`). Missing, inactive, **or caller not a member**
     (`teams.IsMemberAsync(currentUser.UserId, applicationId)`, no Admin bypass) → `ValidationException` on `applicationId`
     with the same message as 05_02 ("Application not found or not available to you."). Hoist that message to a shared
     constant (e.g. `Incidents/IncidentMessages.ApplicationUnavailable`) and use it from both handlers.
  2. `title.Trim()` shorter than `MinSearchLength` (3) → return `[]` without querying (OQ 1).
  3. `ids = await search.FindOpenAsync(applicationId, title, MaxResults /* 5 */, ct)`; empty → `[]`.
  4. Project the summaries for `ids` with `IncidentProjections.Summary` (`AsNoTracking`, `Where(ids.Contains(i.Id))`),
     then re-order in memory to the order of `ids` (the rank order).
  5. Return the list. No logging beyond the pipeline's (read path, called on every keystroke batch).

`Incidents/IncidentProjections` — `IQueryable<IncidentSummaryDto> Summaries(IAppDbContext db)` (or an equivalent
query-building helper): `Incidents` left-joined to `Applications`, `Users` (reporter, assignee) and `Teams` (assigned team)
by key, because `Incident` has no navigation properties. Fields per API-CONTRACT §4 `IncidentSummary`: `number` as
`"INC-{n}"` (format in memory after projection if EF can't translate `IncidentNumber.ToString()`), `rowVersion` base64,
`commentCount` = 0 until BE-08 (existing deferred item). BE-07 reuses this for `GET /incidents`.

### Infrastructure
`Persistence/Search/SqlSimilarIncidentSearch : ISimilarIncidentSearch` (scoped, registered in `AddInfrastructure`):
- One parameterized raw query through `AppDbContext.Database.SqlQuery<Guid>` (interpolated `FormattableString`, so every value is a
  `SqlParameter` — never string concatenation):
  ```sql
  SELECT TOP (@limit) i.Id AS Value
  FROM FREETEXTTABLE(dbo.Incidents, Title, @title) AS ft
  JOIN dbo.Incidents AS i ON i.Id = ft.[KEY]
  WHERE i.ApplicationId = @applicationId AND i.Status <> 'Closed'
  ORDER BY ft.RANK DESC, i.CreatedAt DESC
  ```
  No `top_n_by_rank` argument: it would cut before the application/status filter.
- Add a span on `InfrastructureTelemetry.Source` (`"incident.similar_search"`) with the application id tag (not the title).
- `@title` is the raw trimmed text; `FREETEXT` accepts any characters (quotes, `*`, `(`, `NEAR`, …) without syntax errors. If the
  input contains only noise words, SQL Server returns no rows.

### Api
`Controllers/IncidentsController`:
```csharp
[HttpGet("similar")]
[Authorize(Policy = Policies.CanReportIncident)]
[ProducesResponseType<IReadOnlyList<IncidentSummaryDto>>(StatusCodes.Status200OK)]
// 400, 401, 403, 422
public Task<IReadOnlyList<IncidentSummaryDto>> Similar([FromQuery] Guid? applicationId, [FromQuery] string? title, CancellationToken ct)
```
Maps `applicationId ?? Guid.Empty`, `title ?? string.Empty` to the query (validator → 422). A non-GUID `applicationId` fails binding → 400.
The literal `similar` segment takes routing precedence over BE-07's `{number}` parameter route.

### Tests support
`tests/IncidentHub.Api.IntegrationTests/Support/IncidentSeeder` — bulk-inserts N incidents for an application with
`SqlBulkCopy` into `dbo.Incidents` (omitting `Number` so the sequence default applies; distinct titles from a word list, a mix of
statuses so the fingerprint index stays satisfied), then waits for full-text population
(`FULLTEXTCATALOGPROPERTY('ftIncidentHub','PopulateStatus') = 0` and the item count reached, poll with timeout).
The existing helper from 04_02's `FullTextSearch_Title_ReturnsMatch` should be reused for the wait.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| GET | `/api/v1/incidents/similar?applicationId=&title=` | — | `200` `IncidentSummary[]` (0–5, best match first) | 400 non-GUID `applicationId`; 401; 403 (policy); 422 missing `applicationId`/`title`, title > 200, unknown / inactive / not-a-member application |

`IncidentSummary` already exists in API-CONTRACT §4 and the endpoint in §5 — no shape change. Update `API-CONTRACT.md` (additive notes on the
§5 row): caller must belong to the application's owning team (Admin included), otherwise `422` on `applicationId`; title shorter than 3
characters returns `[]`; ordered by similarity; incidents appear a few seconds after they are reported (full-text change tracking is
asynchronous). Regenerate `contracts/swagger.json` with `scripts/generate-openapi.ps1` (new path).

## 5. Acceptance criteria

- [x] Returns only incidents with `Status != Closed` for the given application: a closed match and a match in another application are never returned.
- [x] At most 5 results, ordered by full-text rank (best first, newest first on ties); each item is an `IncidentSummary` matching API-CONTRACT §4.
- [x] Matching uses `FREETEXT` semantics on `Title` (e.g. "checkout fails" finds "Checkout failing after payment"); `Description` is not searched.
- [x] Title shorter than 3 characters after trimming → `200 []` with no database search.
- [x] Missing `applicationId` or `title`, or title > 200 → `422` with camelCase field keys; non-GUID `applicationId` → `400`.
- [x] Caller not on the application's team (Admin included), unknown or inactive application → `422` on `applicationId` with the same message as the report endpoint.
- [x] No token → `401`; token without a role → `403`.
- [x] Input with full-text operators/quotes (`"`, `*`, `(`, `AND`, `NEAR`) never causes a 500; the query is parameterized.
- [x] Response time < 300 ms p95 against a seeded 50k-row `Incidents` table (opt-in performance test, see §6).
- [x] Handler projects straight to DTOs with `AsNoTracking()` (no aggregate loads); Application references no SQL Server provider type (ADR 0001); controller is mapping only.
- [x] `API-CONTRACT.md` and `swagger.json` updated; build (warnings as errors) + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Incidents/FindSimilarIncidents/FindSimilarIncidentsValidatorTests` (`TestValidate`):
`Validate_ValidQuery_NoErrors`, `Validate_EmptyApplicationId_HasApplicationIdError`, `Validate_EmptyTitle_HasTitleError`,
`Validate_TitleTooLong_HasTitleError`.

`tests/IncidentHub.Api.IntegrationTests/Incidents/FindSimilarIncidentsHandlerTests` — `[Trait("Category","Integration")]`, real SQL
container via `ApiFactory` (DB-backed handler tests live here, 05_02 OQ 11); `IAppTeamReader` via NSubstitute:
`Handle_NotMember_ThrowsValidationOnApplicationId`, `Handle_InactiveApplication_ThrowsValidationOnApplicationId`,
`Handle_TitleShorterThanThree_ReturnsEmptyWithoutSearch` (substitute `ISimilarIncidentSearch`, assert no call),
`Handle_Matches_ReturnsSummariesInRankOrder`.

`tests/IncidentHub.Api.IntegrationTests/Persistence/SqlSimilarIncidentSearchTests` — `[Trait("Category","Integration")]`:
`FindOpenAsync_ClosedMatch_Excluded`, `FindOpenAsync_OtherApplicationMatch_Excluded`, `FindOpenAsync_MoreThanLimit_ReturnsOnlyTheLimit`,
`FindOpenAsync_InflectedWord_Matches` ("fails" ↔ "failing"), `FindOpenAsync_DescriptionOnlyMatch_NotReturned`,
`FindOpenAsync_FullTextOperatorsInInput_DoesNotThrow` (`[Theory]` with `"`, `*`, `(`, `AND`, `NEAR`, `'; DROP TABLE`).

`tests/IncidentHub.Api.IntegrationTests/Incidents/SimilarIncidentsTests` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `Similar_OpenMatches_Returns200WithSummaries`
- `Similar_OnlyClosedMatches_ReturnsEmpty`
- `Similar_ShortTitle_ReturnsEmpty`
- `Similar_MissingParameters_Returns422WithFieldErrors`, `Similar_NonGuidApplicationId_Returns400`
- `Similar_NonMember_Returns422`, `Similar_AdminNotMember_Returns422`, `Similar_UnknownApplication_Returns422`
- `Similar_NoToken_Returns401`, `Similar_NoRole_Returns403`

`tests/IncidentHub.Api.IntegrationTests/Performance/SimilarIncidentsPerformanceTests` — `[Trait("Category","Integration")]` **and**
`[Trait("Category","Performance")]`; excluded from the default run with `dotnet test --filter Category!=Performance`, run on demand with
`--filter Category=Performance`:
- `Similar_50kSeededRows_P95Under300Ms`: `IncidentSeeder` seeds 50,000 incidents (spread over a few applications, the target one with
  ≥ 10,000), waits for full-text population, 10 warm-up calls, then 100 calls through the HTTP client with varied titles; assert the 95th
  percentile of the measured durations < 300 ms. The test prints p50/p95/max.

Add the `--filter Category!=Performance` note to the CLAUDE.md Commands section (see OQ 8).

## 7. Out of scope

- Advanced duplicate detection beyond full-text (Phase 3 "future" in BACKEND-SPECS, not a scheduled BE item).
- Searching `Description` or the list endpoint's text filter `q` → BE-07.
- Real `commentCount` in the summaries → BE-08 (already deferred).
- Rate limiting the endpoint → the existing unassigned cross-cutting rate-limiting item (its note now names this endpoint too).
- Caching the results in Redis (the query is cheap and results must reflect new incidents).

## 8. Open questions

Decided with the user on 2026-10-13:

1. **Layer:** the full-text query lives in Infrastructure behind `ISimilarIncidentSearch` (ranked ids); the handler projects summaries with
   core EF. No new ADR.
2. **Matching:** `FREETEXTTABLE` on `Title` only, ordered by `RANK DESC`, then `CreatedAt DESC`, top 5.
3. **Access:** not a member (Admin included), unknown or inactive application → `422` on `applicationId`, same as `POST /incidents`.
4. **Performance test:** opt-in integration test tagged `Category=Performance`, 50k rows bulk-seeded, p95 < 300 ms over 100 calls.

Confirmed with the user on 2026-10-13 (proposals 5-8):

5. Minimum search length 3 characters (trimmed): shorter → `200 []` rather than `422`, so the SPA can call while typing without error
   handling. The contract does not state a minimum.
6. Policy `CanReportIncident` (any role), since the endpoint serves the report form. BACKEND-SPECS does not name a policy.
7. `FREETEXT` change tracking is `AUTO` (04_02), so a just-reported incident is found only after population catches up (usually seconds).
   This is accepted, and the contract note documents it; the duplicate-fingerprint `409` on report (05_02) still covers exact duplicates
   immediately.
8. Excluding `Category=Performance` from a plain `dotnet test` needs either a documented filter or a `.runsettings`/`Directory.Build.props`
   default. Proposed: a documented filter in CLAUDE.md Commands, no default exclusion mechanism.
