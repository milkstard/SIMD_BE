# Spec 03_03: Application catalog endpoints

> Status: **Draft** · Source: BE-03 in docs/BACKEND-SPECS.md · Part 03 of 03

## 1. Purpose & scope

Manage and read the application catalog: `GET/POST /api/v1/applications`, `GET/PUT /api/v1/applications/{id}`.
Applies the team-scoped visibility and Admin write rules, and closes the BE-03 acceptance criteria end to end.

## 2. Dependencies

BE-02; **03_01** (entities, migration); **03_02** (teams exist; `IfMatchFilter`, idempotency, validation pipeline, `Paged<T>` helpers introduced there).

## 3. Design

Use cases under `Application/Apps/<UseCase>/` (command/query + handler + validator):

| Use case | Type | Notes |
|---|---|---|
| `ListApps` | Query | `AsNoTracking()` + `Select` to `ApplicationDto`; filters `isActive`, cursor paging; ordered by `Name` |
| `GetApp` | Query | by `Id`; 404 if missing **or not visible** |
| `CreateApp` | Command | Admin; validates `OwningTeamId` and `EscalationUserId` exist; `Code` unique → 409 |
| `UpdateApp` | Command | Admin; `If-Match` → `RowVersion`; stale → 409; full replace incl. `isActive` |

- Validators: `Name` required ≤200; `Code` required, `^[A-Za-z0-9_-]{2,20}$`, normalized upper-case; `Environments` non-empty, distinct, each a valid `Environment`;
  `OwningTeamId` non-empty.
- Handler checks (not validators, they need data): owning team exists → else `422` on `owningTeamId`; escalation user exists → else `422` on `escalationContactId`; duplicate `Code` → `409`.
- Visibility (reads): Admin sees all; other roles see only applications whose owning team they belong to (via `TeamMembers`), consistent with existence hiding in entra-id spec §6.1 (non-visible → `404`). See Open question 2.
- Api: `Controllers/ApplicationsController`; `POST`/`PUT` use `[Authorize(Policy = Policies.CanManageApplications)]` plus an explicit Admin requirement for create (Open question 1);
  reads use `CanViewIncident`-level auth (any role) with the filtering above.
- Deactivation (`isActive=false`) never deletes; deactivated apps stay readable (history is kept) but are excluded from the default list unless `includeInactive=true`... (Open question 4).
- Errors via `ProblemDetailsMiddleware`; no hand-built responses. Structured logs: `"Application {Code} created"`.
- Cache: none in this part. `AppStatus` is derived elsewhere (BE-11) and **not** exposed here.

## 4. Endpoints & DTOs

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `/applications` | — | `200` `Paged<ApplicationDto>` (Open question 5) |
| POST | `/applications` | `CreateApplicationRequest` | `201` `ApplicationDto`, `Location`, `Idempotency-Key` honoured |
| GET | `/applications/{id}` | — | `200` `ApplicationDto` + `ETag` |
| PUT | `/applications/{id}` | `UpdateApplicationRequest`, `If-Match` | `200` `ApplicationDto` + `ETag` |

Existing contract model:

```ts
interface Application { id; name; code; owningTeam: TeamRef; escalationContact: UserRef | null; environments: Environment[]; isActive: boolean; }
```

Proposed additive changes to `API-CONTRACT.md` (same PR; **breaking-free**, new optional-field rule applies):
- Add `rowVersion: string` to `Application` (DATABASE.md and CLAUDE.md require `If-Match` on `Applications` writes, but the DTO has no `rowVersion`).
- Define `CreateApplicationRequest { name; code; owningTeamId; escalationContactId?: string | null; environments: Environment[]; isActive?: boolean (default true) }` and `UpdateApplicationRequest` (same fields, `isActive` required).
- Document `409` (duplicate code / stale) and `422` field errors for these routes.
- Regenerate `contracts/swagger.json` with `scripts/generate-openapi.ps1`.

## 5. Acceptance criteria

- [ ] `POST /applications` by a non-Admin (Reporter, Responder, TeamLead) → `403`; no token → `401`.
- [ ] Round-trip: `POST` then `GET /applications/{id}` returns identical data (name, code, owningTeam, escalationContact, environments, isActive).
- [ ] `GET /applications` includes the created application for an Admin; for a non-member user it does not (and `GET /{id}` → `404`).
- [ ] Duplicate `code` (case-insensitive) → `409`; nonexistent `owningTeamId`/`escalationContactId` → `422` with camelCase keys.
- [ ] `PUT` with stale `If-Match` → `409`; with current value → `200` and a new `rowVersion`/`ETag`.
- [ ] `PUT` unknown id → `404`; `Idempotency-Key` repeat on `POST` returns the original result, one row.
- [ ] `API-CONTRACT.md` and `contracts/swagger.json` updated; migration from 03_01 applies cleanly with these endpoints running against it.

## 6. Test plan

Application.UnitTests
- `CreateAppValidator_InvalidCode_Fails`, `..._EmptyEnvironments_Fails`, `..._DuplicateEnvironments_Fails`, `..._ValidInput_Passes`; same for `UpdateAppValidator`.
- `CreateAppHandler_DuplicateCode_ReturnsConflict`, `..._UnknownTeam_ReturnsValidationError`, `..._UnknownEscalationUser_ReturnsValidationError`, `..._Valid_PersistsAndReturnsDto`.
- `UpdateAppHandler_StaleRowVersion_ReturnsConflict`, `GetAppHandler_NotVisibleToUser_ReturnsNotFound`.

Api.IntegrationTests (`[Trait("Category","Integration")]`, `Apps/ApplicationsEndpointsTests`)
- `PostApplications_NonAdminRole_Returns403` (Theory: Reporter, Responder, TeamLead), `PostApplications_Admin_Returns201`.
- `PostThenGetApplication_ReturnsIdenticalData`, `PostApplications_DuplicateCodeDifferentCase_Returns409`, `PutApplications_StaleIfMatch_Returns409`,
  `GetApplications_UserNotInOwningTeam_ExcludesApplication`, `GetApplicationById_UserNotInOwningTeam_Returns404`,
  `PostApplications_SameIdempotencyKey_CreatesOnce`, `PostApplications_UnknownTeam_Returns422`.
- Migration test from 03_01 re-run in the same fixture to confirm "applies cleanly against an empty database".

## 7. Out of scope

Deleting applications; `AppStatus`/dashboard data (BE-11); SLA policy and escalation behaviour (BE-12); team membership management; SignalR group changes when an app's owning team changes (document in BE-09).

## 8. Open questions

1. **Write authorization conflict**: BACKEND-SPECS (AC) says `POST /applications` requires **Admin**; entra-id spec §6 says `CanManageApplications` = TeamLead (own app only) + Admin, and applies to "application + team + SLA policy endpoints". Proposal: `POST` Admin only; `PUT` Admin, plus TeamLead only for apps owned by a team they lead (`TeamMembers.Role = TeamLead`).
2. **Read visibility**: API-CONTRACT groups `/applications` under "Admin (Admin role)" but the frontend needs the app list to report an incident (any role, restricted to the user's teams per entra-id D6/§6). Confirm: all roles may `GET`, filtered by team membership, Admin sees all.
3. **`escalationContact` field naming**: contract response uses `escalationContact: UserRef`, DATABASE.md uses `EscalationUserId`; request field name `escalationContactId` is proposed here — confirm.
4. **Inactive applications** in `GET /applications`: contract is silent. Proposal: default `isActive=true` only; `includeInactive=true` for Admin. Additive query param.
5. **Paging** of `GET /applications`: see 03_02 Q7 (cursor-paged vs plain array); the frontend picker likely wants the full list.
6. **`Environments` stored values** vs `Incident.Environment` (`Production`, `UAT`, `Development`): DATABASE.md example lists `Staging`. Resolved to the API-CONTRACT enum in 03_01; confirm.
7. **`MonitoredApp` vs `Application` naming** leaks into OpenAPI: route/DTO names use `Application`/`applications`; the C# class stays `MonitoredApp` (03_01 Q1).
8. **Changing `OwningTeamId`** silently changes who can see/act on existing incidents (team check). Allow on `PUT`, or forbid once incidents exist (BE-04+)?
