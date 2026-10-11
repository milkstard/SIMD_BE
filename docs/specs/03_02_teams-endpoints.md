# Spec 03_02: Teams endpoints

> Status: **Implemented** (integration tests written; they need Docker to run) · Source: BE-03 in docs/BACKEND-SPECS.md · Part 02 of 03

## 1. Purpose & scope

Admin-only management of `Team` reference data: list, create, update (`GET/POST /api/v1/teams`, `PUT /api/v1/teams/{id}`).
Teams are created before applications because `Application.OwningTeamId` requires one.

## 2. Dependencies

BE-02 (auth, `ProblemDetailsMiddleware`, policies), **03_01** (Team entity, migration). Needs the MediatR pipeline (`ValidationBehavior`) from `Application/DependencyInjection.cs`
and the `IfMatchFilter` (RowVersion ↔ ETag) from `solution-layout.md` §5.4 — if missing, build them here (Open question 3).

## 3. Design

One use case = command/query + handler + validator under `Application/Teams/<UseCase>/`:

| Use case | Type | Notes |
|---|---|---|
| `ListTeams` | Query | `AsNoTracking()`, projects to `TeamDto`, ordered by `Name`, cursor-paged |
| `CreateTeam` | Command | Idempotency-Key honoured; returns created `TeamDto` |
| `UpdateTeam` | Command | Carries expected `RowVersion` from `If-Match`; stale → 409 |

- Application: `Dtos/TeamDto`, `IAppDbContext` usage only (no EF types leaking to Domain).
- Validators (FluentValidation): `Name` required ≤200; `Email` required, valid format, ≤320; `TeamsChannelUrl` null or absolute `https` URL ≤500.
- Api: `Controllers/TeamsController` (thin, `/api/v1/teams`), request records in `Api/Contracts/`.
  Authorization: `[Authorize(Policy = Policies.CanManageApplications)]` + role check so only **Admin** may write (Open question 1).
  Reads (`GET`) are Admin only as well, unless Open question 2 decides otherwise.
- Errors via `ProblemDetailsMiddleware` only: 422 validation, 409 stale `If-Match`, 404 unknown id, 428/400 missing `If-Match` (Open question 4).
- `Team` names: duplicate names allowed? (Open question 5.)
- Logging: structured, e.g. `"Team {TeamId} updated"`. Pass `CancellationToken` throughout.

## 4. Endpoints & DTOs

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `/teams` | — | `200` `Paged<TeamDto>` (Open question 2) |
| POST | `/teams` | `CreateTeamRequest` | `201` `TeamDto`, `Location: /api/v1/teams/{id}` — **no GET by id exists in the contract** (Open question 6) |
| PUT | `/teams/{id}` | `UpdateTeamRequest`, `If-Match` | `200` `TeamDto` + `ETag` |

```ts
interface Team { id: string; name: string; email: string; teamsChannelUrl: string | null; rowVersion: string; }
interface CreateTeamRequest { name: string; email: string; teamsChannelUrl?: string | null; }
interface UpdateTeamRequest { name: string; email: string; teamsChannelUrl?: string | null; }   // full replace (PUT)
```

`API-CONTRACT.md` currently names only `TeamRef` and lists the routes; **update it in the same change** with the `Team` model, request types, the 409/422 cases, and
regenerate `contracts/swagger.json` via `scripts/generate-openapi.ps1`.

## 5. Acceptance criteria

- [ ] `POST /teams` and `PUT /teams/{id}` by a non-Admin authenticated user → `403`; no token → `401`.
- [ ] Create → list returns the team with identical `name`, `email`, `teamsChannelUrl`.
- [ ] `PUT` with stale `If-Match` → `409` ProblemDetails with `traceId`; with current value → `200` and a new `rowVersion`/`ETag`.
- [ ] Invalid email / non-https URL / blank name → `422` with `errors` keyed in camelCase (`name`, `email`, `teamsChannelUrl`).
- [ ] `PUT` unknown id → `404`.
- [ ] Repeated `POST` with the same `Idempotency-Key` returns the original result and creates one row.
- [ ] `API-CONTRACT.md` and `contracts/swagger.json` updated; CI diff clean.

## 6. Test plan

Application.UnitTests (NSubstitute / TestValidate)
- `CreateTeamValidator_BlankName_Fails`, `..._InvalidEmail_Fails`, `..._HttpUrl_Fails`, `..._ValidInput_Passes`; same for `UpdateTeamValidator`.
- `CreateTeamHandler_ValidCommand_PersistsTeamAndReturnsDto`, `UpdateTeamHandler_UnknownId_ReturnsNotFound`, `UpdateTeamHandler_StaleRowVersion_ReturnsConflict`.

Api.IntegrationTests (`[Trait("Category","Integration")]`, `Teams/TeamsEndpointsTests`)
- `PostTeams_NonAdmin_Returns403` (Reporter, Responder, TeamLead each), `PostTeams_Admin_Returns201WithLocation`.
- `GetTeams_AfterCreate_ReturnsSameData`, `PutTeams_StaleIfMatch_Returns409`, `PutTeams_MissingIfMatch_ReturnsExpectedError`, `PostTeams_InvalidBody_Returns422WithFieldErrors`,
  `PostTeams_SameIdempotencyKey_CreatesOnce`.

## 7. Out of scope

Team deletion; team membership management (add/remove members, roles); Teams/Slack webhook delivery (BE-13); `TeamRef` use in incidents.

## 8. Open questions — resolved

Decided: 1 Admin only for every verb via new policy `CanManageTeams` (`CanManageApplications` unchanged); 2 Admin-only reads; 3 pipeline pieces built here (`IAppDbContext` + ADR 0001, `ValidationBehavior`, `RequireIfMatch`, `ETagResultFilter`, `Idempotent`, `Paged<T>`/keyset cursor, `RedisIdempotencyStore`); 4 missing `If-Match` is `428`, malformed is `400`, stale is `409`; 5 duplicate names allowed; 6 `GET /teams/{id}` added (additive); 7 `Paged<T>` for teams; 8 no outbox event.

Original questions:


1. **Who may write teams**: BACKEND-SPECS only states `POST /applications` requires Admin; `Policies.CanManageApplications` accepts TeamLead + Admin (TeamLead "own app only", which has no meaning for teams). Proposal: Admin only for all team writes.
2. **Who may read `GET /teams`**: API-CONTRACT puts these under "Admin (Admin role)". Reporters need team names only through `TeamRef` embedded in other DTOs. Confirm Admin-only reads.
3. **Infrastructure pieces not yet built**: `IfMatchFilter`, `IdempotencyFilter`/`IIdempotencyStore`, `ValidationBehavior`. Build them in this part (first consumer) or add a prerequisite part?
4. **Missing `If-Match` status**: CLAUDE.md says stale → 409; API-CONTRACT's error table has no 428. Proposal: missing header → `400`... confirm.
5. **Duplicate team names**: DATABASE.md has no unique index on `Teams.Name`. Allow, or add a unique index (schema change)?
6. **No `GET /teams/{id}`** in the contract; `POST` returns `Location`. Add the endpoint (additive) or point `Location` at the list?
7. **Paging** on `GET /teams` and `GET /applications`: contract says lists are cursor-paged `Paged<T>`, but admin reference lists are small. Page them or return a plain array?
8. **Team email changes are not routed through the outbox**; confirm nothing downstream needs an event.
