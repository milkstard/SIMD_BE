# Spec 07_03: Incident edit (PATCH)

> Status: **Draft** · Source: BE-07 in docs/BACKEND-SPECS.md · Part 03 of 03

## 1. Purpose & scope

`PATCH /api/v1/incidents/{id}` — edit title, description, steps to reproduce, severity (with a reason), priority, application and environment.
Requires `If-Match`; stale → `409`. Severity changes recompute the SLA due times with `SlaClock` (never in the controller).

In scope:
- `UpdateIncident` use case (command + handler + validator).
- Partial-update binding that tells "field absent" from "field set to null" (`stepsToReproduce`, `priority`).
- HTML sanitization of `description` / `stepsToReproduce` on write.
- Re-computing the duplicate fingerprint on title/application change; duplicate open incident → `409` with `existingIncidentNumber`.
- Moving an incident to another application (caller must belong to the target team, environment must be offered there).
- Team-membership resource check on the target incident (write half).

No items are deferred to this part from `deferred-items.md` beyond the shared membership check (finished here and in 07_02).

## 2. Dependencies

- **07_01** (`IncidentPermissions`, detail projection), **07_02** (by-id load + `RowVersion` handling pattern, `IncidentRequests.cs`).
- BE-04 (`Incident.Update`, `Incident.ChangeSeverity`, `IncidentChanges`, `Optional<T>`, `IncidentClosedException`), 05_01/05_02 (`SlaClock`, `ISlaPolicyProvider`, `IHtmlSanitizer`, `DuplicateIncidentException`).

## 3. Design

### Application
`Incidents/UpdateIncident/`
- `UpdateIncidentCommand(Guid IncidentId, IncidentChanges Changes, Severity? Severity, string? Reason, byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>` (domain `IncidentChanges` already uses `Optional<T>`).
- `UpdateIncidentValidator` (no data access): at least one field present (else 422 on the body); `title` trimmed 5–200; `description` non-empty, ≤ 20,000; `stepsToReproduce` ≤ 5,000; `priority`/`severity`/`environment` defined; `applicationId` not empty; `reason` required when `severity` is present (1–1,000 chars); `reason` without `severity` is ignored (OQ 3).
- `UpdateIncidentHandler(IAppDbContext db, ICurrentUser currentUser, IAppTeamReader teams, IHtmlSanitizer sanitizer, ISlaPolicyProvider slaPolicies, TimeProvider time, ILogger<...> logger)`:
  1. Load the aggregate tracked; missing or caller not in the application's team → `NotFoundException` (404, as in 07_02).
  2. `IncidentPermissions.CanEdit(...)` false (not Responder+/not the reporter, or Closed) → `ForbiddenException` (403) for role, closed → `IncidentClosedException` (409 from the domain). Severity and priority changes require a Responder-level role (OQ 2).
  3. Set `RowVersion` original value from `ExpectedRowVersion` (stale → 409).
  4. If `ApplicationId` changes: target application must exist, be active and the caller must be a member of its team (no Admin bypass), else `ValidationException` on `applicationId` ("Application not found or not available to you." — the shared `IncidentMessages` constant); the effective `Environment` must be offered by the target application, else `ValidationException` on `environment`. If only `Environment` changes, validate against the current application.
  5. Sanitize `description` and `stepsToReproduce`; empty after sanitizing → `ValidationException` on `description`.
  6. Duplicate pre-check when title or application changes: another open incident with the new fingerprint → `DuplicateIncidentException(existingNumber)`; the unique index stays the race backstop.
  7. `incident.Update(changes, actor, now)`; if `Severity` given: `SlaClock.DueTimes(await slaPolicies.GetAsync(severity), now)` then `incident.ChangeSeverity(severity, reason, ack, resolve, actor, now)` (OQ 1: due times counted from the change time).
  8. One `SaveChangesAsync` → incident, history rows (one per changed field), outbox rows for `IncidentUpdated`.
  9. Log `"Incident {IncidentNumber} updated: {Fields}"` (field names only, no values).
  10. Return the refreshed detail DTO.

### Infrastructure
Outbox mapping for `IncidentUpdated` (if not already added in 07_02): type `IncidentUpdated`, payload = event JSON.

### Api
`Controllers/IncidentsController`:
`[HttpPatch("{id:guid}")] [Authorize(Policy = Policies.CanViewIncident)] [RequireIfMatch]` → `Ok(IncidentDetailDto)`; the role/relation rules live in the handler (CanEdit), not the controller.
`Contracts/UpdateIncidentRequest` — a model whose members record presence: `System.Text.Json` converter / constructor that fills `Optional<T>` (absent → `default`, explicit `null` → present with null). Only `stepsToReproduce` and `priority` accept `null`; `null` on any other field → 422.
`[RequestSizeLimit]` 256 KiB like `POST /incidents`.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| PATCH | `/api/v1/incidents/{id}` | `UpdateIncidentRequest` (only changed fields) | `200` `IncidentDetail` + new `ETag` | 400 malformed body / `If-Match`; 401; 403 caller may not edit (or change severity/priority); 404 unknown or not visible; 409 stale `If-Match`, closed incident, duplicate open incident (`existingIncidentNumber`); 413; 422 validation, severity without `reason`, target application not available, environment not offered; 428 |

`UpdateIncidentRequest` exists in API-CONTRACT §4 — no shape change. Notes to add to the contract: `null` is meaningful only for `stepsToReproduce`/`priority`; an empty body
is `422`; changing `applicationId` requires membership of the target application's team; duplicate-fingerprint `409` applies to PATCH as well. Regenerate `contracts/swagger.json`.

## 5. Acceptance criteria

- [ ] A responder can change any listed field; a Reporter-role reporter can edit title/description/steps of their own open incident but not severity or priority; other callers get `403`; non-members get `404`.
- [ ] Only the fields sent change; absent fields are untouched; `stepsToReproduce: null` and `priority: null` clear the value; one history row per changed field with old and new values; an unchanged value produces no history row and no event.
- [ ] Changing `severity` requires `reason` (422 otherwise), writes a `Severity` history row with the reason as note, and sets `AckDueAt`/`ResolveDueAt` from the new policy (equal to `SlaClock.DueTimes(policy, changeTime)`); the response shows the new due times.
- [ ] Stale `If-Match` → `409` with the incident unchanged; missing → `428`; malformed → `400`.
- [ ] A closed incident cannot be edited (`409`, `invalid-transition` type).
- [ ] Changing the title or application recomputes the fingerprint; colliding with another open incident → `409` with `existingIncidentNumber`.
- [ ] Moving to another application requires membership of its team and an environment it offers (`422` on `applicationId` / `environment` otherwise).
- [ ] `<script>` / `onerror=` in `description` or `stepsToReproduce` is stripped before storage.
- [ ] Incident, history and outbox rows are written by one `SaveChangesAsync`.
- [ ] No logic in the controller; times from `TimeProvider`; contract + `swagger.json` updated; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Incidents/UpdateIncident/UpdateIncidentValidatorTests`: `Validate_EmptyPatch_HasError`, `Validate_TitleTooShort_HasTitleError`, `Validate_SeverityWithoutReason_HasReasonError`, `Validate_DescriptionTooLong_HasDescriptionError`, `Validate_UndefinedPriority_HasPriorityError`, `Validate_ValidPatch_NoErrors`.

`tests/IncidentHub.Api.UnitTests/Contracts/UpdateIncidentRequestTests`: `Deserialize_AbsentField_NotPresent`, `Deserialize_ExplicitNull_PresentWithNull`, `Deserialize_NullTitle_Rejected`.

`tests/IncidentHub.Api.IntegrationTests/Incidents/UpdateIncidentTests` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `Patch_ResponderChangesTitle_Returns200WithHistory`, `Patch_OnlySentFieldsChange`, `Patch_ClearStepsAndPriority_SetsNull`, `Patch_SameValues_NoHistory`
- `Patch_SeverityWithReason_RecomputesDueTimes`, `Patch_SeverityWithoutReason_Returns422`
- `Patch_ReporterEditsOwnTitle_Returns200`, `Patch_ReporterChangesSeverity_Returns403`, `Patch_OtherUser_Returns403`, `Patch_NonMember_Returns404`
- `Patch_StaleIfMatch_Returns409`, `Patch_NoIfMatch_Returns428`, `Patch_MalformedIfMatch_Returns400`, `Patch_ClosedIncident_Returns409`
- `Patch_TitleCollidesWithOpenIncident_Returns409WithExistingNumber`
- `Patch_MoveToAnotherApplication_Returns200`, `Patch_MoveToApplicationOfOtherTeam_Returns422`, `Patch_EnvironmentNotOffered_Returns422`
- `Patch_ScriptInDescription_StoredSanitized`, `Patch_EmptyBody_Returns422`, `Patch_NullTitle_Returns422`
- `Patch_WritesHistoryAndOutboxInOneSave`, `Patch_NoToken_Returns401`

## 7. Out of scope

- Transition and assignment → 07_02; reads → 07_01.
- Re-notifying about an SLA change, escalation recalculation → BE-12.
- Editing comments / attachments → BE-08.
- SignalR `IncidentUpdated` → BE-09; email on update → BE-10.

## 8. Open questions

All proposals; confirm before implementing.

1. **SLA base time.** CLAUDE.md says due times are "computed by `SlaClock` on create and on severity change". Proposal: on a severity change count from the time of the change (the 04_01 `ChangeSeverity` signature takes due times; nothing says "from `CreatedAt`"). Alternative: keep the original `CreatedAt` as the base so the clock does not restart.
2. **Who may edit what.** BACKEND-SPECS only says "edit"; 05_02 OQ 7 defined `canEdit` as Responder/TeamLead/Admin or the reporter (open incident). Proposal: reporters may change title, description and steps; severity, priority, application and environment require a Responder-level role.
3. **`reason` without `severity`.** Proposal: ignore it (no error) — or 422? Proposal: ignore, because the contract says "required when severity changes".
4. **`applicationId` change** is allowed in the contract. Proposal: allowed for Responder+ who belong to both teams; the incident's assignee and assigned team are cleared if they no longer fit? Proposal: leave them as they are (out of scope), and document it.
5. **Severity set to the current value** is a domain no-op (04_01): no history, no due-time change, `reason` still required by the validator. Confirm.
