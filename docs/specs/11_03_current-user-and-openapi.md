# Spec 11_03: Current user (`GET /me`) and deterministic OpenAPI

> Status: **Done** · Source: BE-11 in docs/BACKEND-SPECS.md · Part 3 of 3

## 1. Purpose & scope

- `GET /api/v1/me` returns the signed-in user as `CurrentUser`.
- `contracts/swagger.json` contains every BE-11 endpoint and regenerates identically on every run.

## 2. Dependencies

BE-02 (`ICurrentUser`, auth, `UserDirectory`), BE-07 (`IAppTeamReader.GetTeamIdsForUserAsync`), 11_01 and 11_02 (the endpoints whose contract is regenerated). `scripts/generate-openapi.ps1` and the first `swagger.json` already exist.

## 3. Design

### Application (`Users/GetCurrentUser/`)
`GetCurrentUserQuery` (no fields) + `Handler`: builds `CurrentUserDto(Id, DisplayName, Email, Roles, TeamIds)` from `ICurrentUser` (id, name, email, roles) and `IAppTeamReader.GetTeamIdsForUserAsync(userId)`. No validator (no input). Roles are sorted by their enum order, team ids ascending, so the response is stable.
`Id` is the internal `User.Id`, not the Entra object id (contract: `UserRef.id`).

### Api
`Controllers/MeController` (`[Authorize]`): `GET me` → `ISender.Send` → `200`. Any signed-in role, no policy beyond authentication. The user projection row is already ensured on sign-in by the existing `UserDirectory`; this endpoint adds no write.

### OpenAPI determinism
- Review `scripts/generate-openapi.ps1` output for sources of drift (order of paths/tags, enum ordering, server URLs, machine-specific values) and fix them in the Swagger setup, not in the script.
- A unit test generates the document twice from the same host and compares the JSON text.
- CI: no workflow file exists in the repository. The "fails on unexpected diff" step (run the script, then `git diff --exit-code contracts/swagger.json`) is documented in the spec and in `CLAUDE.md` as a pipeline task. It is not implemented here (OQ 2).
- Regenerate `contracts/swagger.json` once for all of BE-11 (`/dashboard/app-status`, `/dashboard/summary`, `/me`), additive only.

### Infrastructure / Worker
Nothing.

## 4. Endpoints & DTOs

| Method | Path | Success |
|---|---|---|
| GET | `/me` | `200` `CurrentUser` |

`CurrentUser extends UserRef { roles: Role[]; teamIds: string[] }` already in API-CONTRACT §3; add the `GET /me` notes (ordering, internal id) and keep the endpoint table row. `swagger.json` gains the three BE-11 paths.

## 5. Acceptance criteria

- [ ] `GET /me` returns the caller's internal id, display name, email, roles and the ids of their teams; a user in no team gets `teamIds: []`.
- [ ] Roles come from the token's group mapping, not from `TeamMembers.Role` (consistent with BE-02).
- [ ] No other user's data can be requested (the endpoint takes no id); unauthenticated is `401`.
- [ ] Two generations of the OpenAPI document are byte-identical; the committed `swagger.json` equals a fresh generation.
- [ ] `swagger.json` contains `/dashboard/summary`, `/dashboard/app-status` and `/me`, the enums as strings, and `range` documented with its allowed values.
- [ ] No status-setting endpoint exists for applications (the document has no `AppStatus` in any request body).
- [ ] `dotnet build`, `dotnet format` clean; `API-CONTRACT.md` updated; the script run is recorded in `current-status.md` during implementation.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Users/`: `Handler_ReturnsIdentityRolesAndTeamIds`, `Handler_NoTeams_ReturnsEmptyTeamIds`, `Handler_RolesAndTeamsAreSorted`.
`tests/IncidentHub.Api.IntegrationTests/Users/MeTests` — `[Trait("Category","Integration")]`: `Get_Responder_ReturnsRolesAndTeams`, `Get_Admin_ReturnsAdminRole`, `Get_NoTeam_ReturnsEmptyTeamIds`, `Get_Unauthenticated_Returns401`.
`tests/IncidentHub.Api.UnitTests/OpenApi/`: `Generate_TwiceFromSameHost_IsIdentical`, `Generate_ContainsBe11Endpoints`, `Generate_NoRequestSchemaContainsAppStatus`, `Generate_MatchesCommittedSwagger` (skipped when the file is absent on a fresh clone).

## 7. Out of scope

- A CI workflow or pipeline definition (documented, not built).
- `/me/notifications*` → BE-14.
- Profile editing, notification preference endpoints (`NotificationPrefs`) → BE-13.
- Frontend type generation (consumes `swagger.json`, not part of this repo).

## 8. Open questions

1. **Roles source.** `Role` on `TeamMembers` differs from the token roles; `/me` returns the token roles (as `ICurrentUser`). Confirm that per-team roles are not needed by the frontend now.
2. **CI diff check.** BACKEND-SPECS and CLAUDE.md say CI fails on an unexpected diff but the repo has no workflow file. Add the step in the pipeline outside this repo, or add a workflow file here? Recommendation: document it only.
3. **`lastSeenAt`.** `Users.LastSeenAt` is updated on sign-in by `UserDirectory`; `GET /me` does not touch it. Confirm.


## 9. Decisions (2026-10-14)

1. Roles: `/me` returns the token roles (OQ 1 accepted).
2. CI diff check: documented in `CLAUDE.md` only, no workflow file (OQ 2 accepted).
3. `lastSeenAt`: `GET /me` does not touch it (OQ 3 accepted).
4. Determinism: paths and component schemas are sorted ordinally by a document filter and actions are ordered explicitly, so the output no longer depends on reflection order. This reorders `swagger.json` once (a deviation from "additive only", agreed with the user).
5. `range` is documented with its allowed values (`24h`, `7d`, `30d`, default `7d`) by a parameter filter; the parameter stays a string so an unknown value remains `422`.
