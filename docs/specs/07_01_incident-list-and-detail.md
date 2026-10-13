# Spec 07_01: Incident list & detail (read side)

> Status: **Implemented** · Source: BE-07 in docs/BACKEND-SPECS.md · Part 01 of 03

## 1. Purpose & scope

The read surface for day-to-day triage:
- `GET /api/v1/incidents` — cursor-paged list with filters and sorting.
- `GET /api/v1/incidents/{number}` — full detail for the caller (`allowedTransitions`, `canEdit`, `canAssign`, `ETag`).

In scope:
- `ListIncidents` and `GetIncident` use cases (query + handler + validator where input exists).
- Visibility rule: a caller sees only incidents of applications owned by a team they belong to (the BE-07 **read half** of the
  team-membership resource check; Admin included, as for reporting — OQ 1).
- Reuse of `IncidentProjections.SummaryRows/ToSummary` (06_01) for the list; a by-number detail projection that fills `duplicateOf`.
- Real `allowedTransitions`, `canEdit`, `canAssign` for any incident and caller (shared `IncidentPermissions` helper, also used by 07_02/07_03).
- Index-backed list queries (`IX_Incidents_Status_Severity`, `IX_Incidents_Application_CreatedAt`) checked against the query plan.
- Full-text `q` filter over `Title` and `Description`.

Pulled in from `deferred-items.md` (all required by BE-07):
- "Team-membership resource check on incident endpoints (all but report)" — the read half here; the write half is delivered by 07_02/07_03.
- "List/detail queries using `IX_Incidents_Status_Severity` / `IX_Incidents_Application_CreatedAt`".

## 2. Dependencies

- BE-02 (`ICurrentUser`, `Policies.CanViewIncident`, `IAppTeamReader`), BE-03 (`Refs`, `KeysetCursor` style, `ETagResultFilter`, `IVersionedDto`).
- BE-04 (`Incident`, `IncidentWorkflow.AllowedTransitions`, indexes, full-text index `ftIncidentHub`).
- 05_02 (`IncidentDetailDto`, `IncidentDetailMapper`, `IncidentsController`), 06_01 (`IncidentProjections`).

## 3. Design

### Application (`IncidentHub.Application`)
`Incidents/IncidentPermissions` (internal static) — one place for the caller-relative flags, replacing the inline logic in `IncidentDetailMapper`:
`CanEdit(Incident-like state, Actor)`, `CanAssign(...)`, `AllowedTransitions` (delegates to `IncidentWorkflow`). Inputs are plain values
(status, reporterId, assigneeId, actor) so the same code runs on an aggregate (07_02/07_03) and on a projected row (here).
Rules as decided in 05_02 OQ 7: `canAssign` = Responder/TeamLead/Admin and not Closed; `canEdit` = those roles, or the reporter, while not Closed.

`Incidents/ListIncidents/`
- `ListIncidentsQuery` — sealed record with the filters of API-CONTRACT §5: `IReadOnlyList<IncidentStatus> Status`, `IReadOnlyList<Severity> Severity`,
  `Guid? ApplicationId`, `AppEnvironment? Environment`, `string? AssigneeId` (`Guid` text, `me`, `unassigned`), `Guid? TeamId`, `string? ReporterId` (`Guid` text or `me`),
  `bool? SlaBreached`, `string? Q`, `DateTimeOffset? CreatedFrom`, `DateTimeOffset? CreatedTo`, `IncidentSort Sort`, `string? Cursor`, `int Limit`, `bool IncludeTotal`
  `: IRequest<Paged<IncidentSummaryDto>>`.
- `IncidentSort` enum: `CreatedAtAsc`, `CreatedAtDesc` (default), `Severity` (most severe first), `UpdatedAtDesc` — mapped from `createdAt|-createdAt|severity|-updatedAt`.
- `ListIncidentsValidator`: `limit` 1–100 (default 25), `q` ≤ 200 chars, `createdFrom <= createdTo`, `assigneeId` is `me`/`unassigned`/GUID, `reporterId` is `me`/GUID, `cursor` decodes (else 422 on `cursor`), enum members defined.
- `ListIncidentsHandler(IAppDbContext db, ICurrentUser currentUser, IAppTeamReader teams, IIncidentTextSearch search)`:
  1. `appIds = teams.GetApplicationIdsForUserAsync(userId)`; the query is always restricted with `ApplicationId IN appIds` (an explicit `applicationId` outside the set returns an empty page, not 403 — no existence leak; OQ 1).
  2. Build one `IQueryable<Incident>` of filters (`AsNoTracking`, no aggregate loads), `me` resolved to `currentUser.UserId`, `unassigned` = `AssigneeId == null`, `slaBreached` = `IsSlaBreached`, dates on `CreatedAt`.
  3. `q`: ids from `IIncidentTextSearch.FindIdsAsync(q, appIds, ct)` (Infrastructure, `FREETEXTTABLE` over `Title, Description`, capped — OQ 4) then `Where(ids.Contains(i.Id))`; empty result → empty page.
  4. Keyset paging on `(sortKey, Id)`: fetch `limit + 1`, `nextCursor` from the last returned row, `null` on the last page. Cursor is opaque base64url JSON like `KeysetCursor` (extended with the sort value; invalid or sort-mismatched cursor → 422).
     `severity` sorts by a rank expression (Critical < High < Medium < Low), then `CreatedAt desc`, then `Id`.
  5. `Select` through `IncidentProjections.SummaryRows` → `ToSummary`. `totalCount` only when `includeTotal=true` (separate `CountAsync` on the same filter).
- `Paged<IncidentSummaryDto>` reuses the existing `Paged<T>`.

`Incidents/GetIncident/`
- `GetIncidentQuery(string Number)` → `IncidentDetailDto`. `Number` parsed with `IncidentNumber.TryParse`; malformed → 404 (same as unknown).
- `GetIncidentHandler`: project the detail row (`AsNoTracking`, joins for refs and the `duplicateOf` number) by `Number`; not found **or caller not a member of its application's team** → `NotFoundException` (404, API-CONTRACT: "not found or not visible"). Compute flags with `IncidentPermissions`. `commentCount` = 0, `attachments` = `[]` until BE-08.
- `IncidentDetailMapper` keeps its create-time helper but delegates flag logic to `IncidentPermissions`.

`Abstractions/IIncidentTextSearch` — `Task<IReadOnlyList<Guid>> FindIdsAsync(string text, IReadOnlyCollection<Guid> applicationIds, int limit, CancellationToken ct)`.

### Infrastructure
`Persistence/Search/SqlIncidentTextSearch` — parameterized `FREETEXTTABLE(dbo.Incidents, (Title, Description), @q)` joined to `dbo.Incidents` filtered by application ids (via a table-valued parameter or `OPENJSON` of a JSON array parameter — no string concatenation), `TOP (@limit)` by rank. Span `"incident.text_search"` on `InfrastructureTelemetry.Source` (no query text tag). Registered in `AddInfrastructure`.

### Api
`Controllers/IncidentsController` (thin):
- `[HttpGet] [Authorize(Policy = Policies.CanViewIncident)] List([FromQuery] ListIncidentsRequest request)` → `Ok(Paged<IncidentSummaryDto>)`.
- `[HttpGet("{number}")] [Authorize(Policy = Policies.CanViewIncident)] Get(string number)` → `Ok(IncidentDetailDto)`; `ETagResultFilter` adds the `ETag`.
- `Contracts/IncidentRequests.cs`: `ListIncidentsRequest` binds repeated `status`/`severity` params; strings for `assigneeId`/`reporterId`; `sort` as the contract tokens (unknown token → 400 by binding, like other enums).
- Routing: the literal `similar` segment (06_01) keeps precedence over `{number}`.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Success | Errors |
|---|---|---|---|
| GET | `/api/v1/incidents` | `200` `Paged<IncidentSummary>` | 400 malformed binding (non-GUID id, unknown enum/sort); 401; 403 (no role); 422 invalid `limit`/`cursor`/date range/`q` too long |
| GET | `/api/v1/incidents/{number}` | `200` `IncidentDetail` + `ETag` | 401; 403; 404 unknown, malformed number, or not visible |

DTOs and the endpoints already exist in API-CONTRACT §4/§5; no shape change. Additive notes to add to `API-CONTRACT.md` (same change):
visibility rule (only incidents of applications owned by the caller's teams, Admin included), `q` searches title and description with full-text semantics
(a just-reported incident can take a few seconds to match), `sort=severity` order, `422` cases. Regenerate `contracts/swagger.json`.

## 5. Acceptance criteria

- [x] `GET /incidents` returns `Paged<IncidentSummary>`; default sort `-createdAt`; `limit` default 25, max 100; `nextCursor` null on the last page; walking all pages returns every visible incident exactly once, with no duplicates or gaps, for each sort.
- [x] Every filter works alone and combined: `status`/`severity` (repeatable), `applicationId`, `environment`, `assigneeId` (GUID, `me`, `unassigned`), `teamId` (assigned team), `reporterId` (GUID, `me`), `slaBreached`, `q`, `createdFrom`/`createdTo`, `includeTotal`.
- [x] A caller never receives an incident of an application whose team they do not belong to (list, detail, `q`, explicit `applicationId`, cursor tampering), Admin included.
- [x] List queries use `AsNoTracking()` + `Select` projection — no aggregate loads; the query plan for the main filter combinations (status+severity, application+date, default list) uses `IX_Incidents_Status_Severity` / `IX_Incidents_Application_CreatedAt` (no clustered-index scan on the 50k-row seed) — verified by an opt-in check.
- [x] `GET /incidents/{number}` returns the full `IncidentDetail` with `ETag` = `"<rowVersion>"`, `duplicateOf` filled for a closed duplicate, and `404` for unknown, malformed, or not-visible numbers (identical response).
- [x] `allowedTransitions` in the detail response equals `IncidentWorkflow.AllowedTransitions` for the caller's role and relation (Responder, TeamLead, Admin, Assignee, Reporter, other) for every status; `canEdit` / `canAssign` follow 05_02 OQ 7.
- [x] No token → `401`; token without a role → `403`.
- [x] Controller maps only; no `DateTime.UtcNow`; `API-CONTRACT.md` and `swagger.json` updated; build (warnings as errors) + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Incidents/`
- `ListIncidentsValidatorTests`: `Validate_ValidQuery_NoErrors`, `Validate_LimitOutOfRange_HasLimitError`, `Validate_DateRangeReversed_HasError`, `Validate_BadAssigneeToken_HasAssigneeIdError`, `Validate_QTooLong_HasQError`.
- `IncidentPermissionsTests`: `AllowedTransitions_EachRoleAndRelation_MatchesWorkflow` (`[Theory]`), `CanEdit_ReporterOnOpenIncident_True`, `CanEdit_ClosedIncident_False`, `CanAssign_ReporterRole_False`.
- Cursor encode/decode round-trip and mismatch tests for the sort-aware cursor.

`tests/IncidentHub.Api.IntegrationTests/Incidents/` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `ListIncidentsTests`: `List_Default_ReturnsNewestFirstVisibleOnly`, `List_PagesWithCursor_ReturnsEveryIncidentOnce` (each sort), `List_FilterByEachField_ReturnsMatches` (`[Theory]`), `List_AssigneeMe_ReturnsOwnAssigned`, `List_Unassigned_ReturnsOnlyUnassigned`, `List_Q_FindsTitleAndDescription`, `List_NonMemberApplication_ReturnsEmpty`, `List_AdminNotMember_SeesNothingOfThatApplication`, `List_IncludeTotal_ReturnsCount`, `List_InvalidLimit_Returns422`, `List_TamperedCursor_Returns422`, `List_NoToken_Returns401`, `List_NoRole_Returns403`.
- `GetIncidentTests`: `Get_ByNumber_ReturnsDetailWithETag`, `Get_ClosedDuplicate_ReturnsDuplicateOf`, `Get_UnknownNumber_Returns404`, `Get_MalformedNumber_Returns404`, `Get_NonMember_Returns404`, `Get_AllowedTransitions_MatchWorkflowPerRole` (`[Theory]`), `Get_NoToken_Returns401`.
- `Persistence/SqlIncidentTextSearchTests`: `FindIds_TitleMatch_Returns`, `FindIds_DescriptionMatch_Returns`, `FindIds_OutsideApplicationIds_Excluded`, `FindIds_OperatorsInInput_DoesNotThrow`.
- `Performance/ListIncidentsQueryPlanTests` — `[Trait("Category","Integration")]` and `[Trait("Category","Performance")]`: seeds 50k rows with `IncidentSeeder`, captures the plan (`SET SHOWPLAN_XML`) for the main filter combinations and asserts the expected indexes are used; prints list p95.

## 7. Out of scope

- `PATCH`, transitions, assignment → 07_02, 07_03.
- Comments, attachments, real `commentCount`/`attachments`, the history endpoint → BE-08 (already deferred).
- SignalR events → BE-09.
- Dashboard aggregates → BE-11; CSV export → BE-16.
- Rate limiting → existing unassigned deferred item.

## 8. Open questions

Decided with the user on 2026-10-13:

1. **Visibility:** the list is scoped to the caller's own teams' applications, Admin included (no bypass); detail of a non-visible incident is `404`; an explicit foreign `applicationId` returns an empty page.
2. **Reporter role:** a Reporter-only user sees all incidents of their team's applications (`reporterId=me` is the narrow view).
3. **Sort tokens:** exactly `createdAt|-createdAt|severity|-updatedAt`; `severity` = most severe first.
4. **`q`:** `FREETEXTTABLE` over `Title, Description` through `IIncidentTextSearch`, top 500 ranked ids, then the normal filters/sort/keyset paging among them.
5. **`teamId`** filters `AssignedTeamId`.
6. **Detail** is addressed by number only (`INC-1042`).
7. **Query-plan test:** asserts the expected indexes only for selective filters (status+severity, one application + date range); the default list is reported (plan summary + p95), not asserted.
