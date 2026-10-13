# Spec 07_02: Incident transition & assignment

> Status: **Implemented** · Source: BE-07 in docs/BACKEND-SPECS.md · Part 02 of 03

## 1. Purpose & scope

The two workflow write endpoints, both guarded by `If-Match`:
- `POST /api/v1/incidents/{id}/transitions` — moves an incident along the state machine.
- `POST /api/v1/incidents/{id}/assignment` — assigns a user and/or a team, or unassigns.

In scope:
- `TransitionIncident` and `AssignIncident` use cases (command + handler + validator).
- Optimistic concurrency through `Incident.RowVersion` (`If-Match`), 409 on stale.
- Team-membership resource check for the write actions (the write half of the deferred item).
- Response is the refreshed `IncidentDetailDto` (+ new `ETag`).
- Outbox rows for `IncidentTransitioned` / `IncidentAssigned` events (OQ 3).

Pulled in from `deferred-items.md`: the write half of "Team-membership resource check on incident endpoints (all but report)".
The read half is delivered by 07_01.

## 2. Dependencies

- **07_01** (`IncidentPermissions`, by-id detail projection, `IncidentsController`).
- BE-02 (`Policies.CanTransitionIncident`, `Policies.CanAssignIncident`, `IAppTeamReader`, `ICurrentUser.ToActor`), BE-03 (`RequireIfMatch`, `ETagResultFilter`).
- BE-04 (`Incident.TransitionTo`, `Incident.Assign`, exceptions → 409/403/422), 05_01 (outbox written in the same `SaveChangesAsync`).

## 3. Design

### Application
`Incidents/TransitionIncident/`
- `TransitionIncidentCommand(Guid IncidentId, IncidentStatus To, string? Note, CloseReason? CloseReason, Guid? DuplicateOfId, byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>`.
- `TransitionIncidentValidator` (no data access): `to` defined; `note` ≤ `Incident.NoteMaxLength` (1,000); `duplicateOfId` not empty when present. Requirement checks that depend on the status (note for Resolved/Reopened, `closeReason` for Closed, `duplicateOfId` for Duplicate) stay in the domain (`TransitionRequirementException` → 422).
- `TransitionIncidentHandler(IAppDbContext db, ICurrentUser currentUser, IAppTeamReader teams, TimeProvider time, ILogger<...> logger)`:
  1. Load the aggregate **tracked** with history by `IncidentId`; missing → `NotFoundException` (404).
  2. Caller not a member of the incident's application team (no Admin bypass) → `NotFoundException` (same 404, consistent with 07_01 — OQ 1).
  3. `db.Entry(incident).Property(i => i.RowVersion).OriginalValue = ExpectedRowVersion` so a stale value fails the `UPDATE` → `DbUpdateConcurrencyException` → 409 (ADR 0001). An early compare against the loaded `RowVersion` returns the 409 without touching the database.
  4. `incident.TransitionTo(To, currentUser.ToActor(), Note, time.GetUtcNow(), CloseReason, DuplicateOfId)`.
     If `DuplicateOfId` is given, verify that incident exists, is in an application the caller can see, and is not the incident itself → else `ValidationException` on `duplicateOfId`.
  5. One `SaveChangesAsync` → incident + history rows + outbox rows.
  6. Log `"Incident {IncidentNumber} moved from {From} to {To}"`.
  7. Return the detail DTO (read back with the by-id projection from 07_01 after the save, so `rowVersion` is the new one).

`Incidents/AssignIncident/`
- `AssignIncidentCommand(Guid IncidentId, Guid? AssigneeId, Guid? TeamId, byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>`.
- `AssignIncidentValidator`: ids not empty when present.
- `AssignIncidentHandler`: same load / membership / `RowVersion` steps; then checks (OQ 2): the assignee exists in `Users` and belongs to the incident's application team; the team exists. Failure → `ValidationException` on `assigneeId` / `teamId`. `incident.Assign(AssigneeId, TeamId, actor, now)`; no change → still `200` with the unchanged detail (no history, no event). `IncidentClosedException` → 409 from the domain.
  The `assignment` body replaces both fields (`null` = clear), per the contract.

### Infrastructure
- Persistence: confirm `RowVersion` is configured as `IsRowVersion()` (04_02) and that `AppDbContext` surfaces `DbUpdateConcurrencyException` (mapped to 409 in `ProblemDetailsMiddleware`, already present).
- Outbox: map `IncidentTransitioned` and `IncidentAssigned` to `OutboxMessage` rows next to `IncidentReported` (05_01 event→outbox mapper). Types `IncidentTransitioned`, `IncidentAssigned`; payload = the event record as JSON (OQ 3). No dispatcher yet (BE-10).

### Api
`Controllers/IncidentsController`:
- `[HttpPost("{id:guid}/transitions")] [Authorize(Policy = Policies.CanTransitionIncident)] [RequireIfMatch]` → `TransitionIncidentCommand` with `HttpContext.GetIfMatchRowVersion()` → `Ok(IncidentDetailDto)`.
- `[HttpPost("{id:guid}/assignment")] [Authorize(Policy = Policies.CanAssignIncident)] [RequireIfMatch]` → `AssignIncidentCommand` → `Ok(IncidentDetailDto)`.
- `Contracts/IncidentRequests.cs`: `TransitionRequest(To?, Note, CloseReason?, DuplicateOfId?)`, `AssignRequest(AssigneeId, TeamId)` — a missing `to` maps to an undefined enum value so the validator answers 422.
- `ProblemDetailsMiddleware`: no new mapping expected (`InvalidTransition` 409, `TransitionNotPermitted` 403, `TransitionRequirement` 422, `IncidentClosed` 409 exist from 04_01). Add a test that a stale `If-Match` yields `409` with `Conflict`.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| POST | `/api/v1/incidents/{id}/transitions` | `TransitionRequest` | `200` `IncidentDetail` + new `ETag` | 400 malformed `If-Match`/body; 401; 403 role/relation may not make a listed move (title "Transition not permitted") or policy; 404 unknown or not visible; 409 move not in the table / stale `If-Match`; 422 missing `note`/`closeReason`/`duplicateOfId`, bad `duplicateOfId`; 428 no `If-Match` |
| POST | `/api/v1/incidents/{id}/assignment` | `AssignRequest` | `200` `IncidentDetail` + new `ETag` | 400; 401; 403 (not Responder+); 404; 409 stale / closed incident; 422 unknown assignee/team or assignee not on the team; 428 |

`TransitionRequest`, `AssignRequest` and the endpoints exist in API-CONTRACT §4/§5 — no shape change. Add notes to `API-CONTRACT.md`: 404 for non-visible incidents,
`duplicateOfId` must be a visible, different incident, assignee must belong to the application's team. Regenerate `contracts/swagger.json`.

## 5. Acceptance criteria

- [x] Each allowed transition for a permitted caller returns `200`, the new `status`, a new `rowVersion`/`ETag`, an updated `allowedTransitions`, and one `Status` history row (old → new, note, actor, time).
- [x] A move not in the table → `409` (`invalid-transition`); a listed move the caller may not make → `403` with title "Transition not permitted"; missing `note` (Resolved, Reopened), `closeReason` (Closed) or `duplicateOfId` (Duplicate) → `422` with camelCase keys.
- [x] Stale `If-Match` → `409` with the entity unchanged; missing → `428`; malformed → `400`. Two concurrent writers with the same ETag: exactly one succeeds.
- [x] Incident row, history rows and the outbox row for the event are written by one `SaveChangesAsync` (rollback verified: an induced failure leaves none of them).
- [x] `AcknowledgedAt`, `ResolvedAt`, `ClosedAt`, `CloseReason`, `DuplicateOfId` are set as specified in 04_01 and visible in the response.
- [x] Assignment sets/clears assignee and team, appends one history row per changed field, `200` with the refreshed detail; assigning the same values is a no-op without history; closed incident → `409`.
- [x] Reporter-only role and non-Responders calling `/assignment` → `403` (policy); unknown assignee/team or assignee outside the application's team → `422` on `assigneeId`/`teamId`.
- [x] A caller outside the application's team gets `404` on both endpoints and nothing changes.
- [x] Controller maps only; handlers use `TimeProvider`; `API-CONTRACT.md` and `swagger.json` updated; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Incidents/`
- `TransitionIncidentValidatorTests`: `Validate_ValidCommand_NoErrors`, `Validate_UndefinedTo_HasToError`, `Validate_NoteTooLong_HasNoteError`.
- `AssignIncidentValidatorTests`: `Validate_EmptyGuidAssignee_HasError`, `Validate_BothNull_NoErrors`.

`tests/IncidentHub.Api.IntegrationTests/Incidents/` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `TransitionIncidentTests`: `Transition_AllowedPerRole_Returns200` (`[Theory]` over the workflow table × caller kind), `Transition_NotInTable_Returns409`, `Transition_NotPermitted_Returns403`, `Transition_ResolvedWithoutNote_Returns422`, `Transition_CloseWithoutReason_Returns422`, `Transition_DuplicateWithoutTarget_Returns422`, `Transition_DuplicateOfUnknownOrSelf_Returns422`, `Transition_StaleIfMatch_Returns409`, `Transition_NoIfMatch_Returns428`, `Transition_MalformedIfMatch_Returns400`, `Transition_ConcurrentSameEtag_OneWins`, `Transition_WritesHistoryAndOutboxInOneSave`, `Transition_NonMember_Returns404`, `Transition_UnknownId_Returns404`, `Transition_NoToken_Returns401`.
- `AssignIncidentTests`: `Assign_UserAndTeam_Returns200WithHistory`, `Assign_Unassign_ClearsFields`, `Assign_SameValues_NoHistory`, `Assign_ClosedIncident_Returns409`, `Assign_ReporterRole_Returns403`, `Assign_AssigneeNotOnTeam_Returns422`, `Assign_UnknownTeam_Returns422`, `Assign_StaleIfMatch_Returns409`, `Assign_NonMember_Returns404`, `Assign_WritesOutboxRow`.
- Rollback test: a failing `SaveChanges` (forced via a faulty interceptor) leaves incident, history and outbox unchanged — extends the 05_01 pattern to the new event types.

## 7. Out of scope

- `PATCH` → 07_03. List/detail reads → 07_01.
- Sending notifications for the outbox rows (assignment email, status-change message) → BE-10 / BE-13 / BE-14; only the rows are written here.
- SignalR `IncidentUpdated` after commit → BE-09.
- SLA breach and escalation transitions by the system → BE-12.
- Comments on transition → BE-08.

## 8. Decisions

Decided with the user on 2026-10-13:

1. **Non-members** get `404` on both endpoints (Admin included), the same response as an unknown id.
2. **Assignee** must be a member of the incident's application's owning team (any team role); an unknown user or non-member is `422` on `assigneeId`. `teamId` may be any existing team; an unknown team is `422` on `teamId`. Any Responder/TeamLead/Admin may assign to any eligible member.
3. **`duplicateOfId`** must exist, differ from the incident and belong to an application the caller can see; otherwise `422` on `duplicateOfId`.
4. **Outbox rows** for `IncidentTransitioned` / `IncidentAssigned` are written now. `AppDbContext.StageOutbox` already maps every domain event generically, so no Infrastructure change is needed; the endpoint tests prove it.
5. A no-op assignment still validates `If-Match` and returns `200` with the unchanged detail and ETag.
6. System transitions (`Actor.System`) are not reachable through the API.

Implementation notes: `IAppDbContext` has no `Entry`, so the stale-write check compares the loaded `RowVersion` with `If-Match` (as `UpdateTeamHandler` does); a writer that commits between load and save makes the `UPDATE` fail with `DbUpdateConcurrencyException` (409). The rollback test uses such a real concurrent save instead of a faulty interceptor.
