# Spec 04_01: Incident aggregate & workflow state machine

> Status: **Implemented** · Source: BE-04 in docs/BACKEND-SPECS.md · Part 01 of 02

## 1. Purpose & scope

Build the `Incident` aggregate in `IncidentHub.Domain` so that **no caller can bypass the workflow**: status changes only
through `Incident.TransitionTo(...)`, every change appends an `IncidentHistory` row, and every change raises a domain event.
Pure domain code, no persistence and no HTTP surface.

In scope:
- Domain building blocks: `AggregateRoot` (domain-event list), `IDomainEvent`, `DomainException` hierarchy.
- Incident enums (`Severity`, `Priority`, `IncidentStatus`, `IncidentSource`, `CloseReason`).
- `IncidentWorkflow` (the transition table + "who" rules) and `Incident.TransitionTo`.
- `Incident` mutation methods that BE-04's events require (report, transition, assign, update/severity change).
- `IncidentHistory` (append-only, created only by the aggregate).
- Domain events `IncidentReported`, `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated`.
- `IncidentFingerprint` (app + normalized title) and `IncidentNumber` (`INC-{n}` formatting/parsing).

Out of scope here: EF mapping, sequence, indexes and migration (04_02); SLA due-time calculation (BE-05/BE-12); handlers and endpoints (BE-05, BE-07).

## 2. Dependencies

- BE-01 (Domain project, `Entity`), BE-02 (`Actor`, `UserRole`), BE-03 (`MonitoredApp`, `AppEnvironment`).
- No earlier parts.

## 3. Design

### Domain (`IncidentHub.Domain`)

`Common/`
- `IDomainEvent` — marker with `DateTimeOffset OccurredAt`.
- `AggregateRoot : Entity` — `IReadOnlyList<IDomainEvent> DomainEvents`, `protected Raise(IDomainEvent)`, `ClearDomainEvents()`.
- `DomainException` (abstract) + `Incidents/` subclasses:
  - `InvalidTransitionException` — the target status is not in the table for the current status (API → **409**).
  - `TransitionNotPermittedException` — the move exists but the actor's role/relation is not allowed (API → **403**).
  - `TransitionRequirementException` — a required input is missing (note for Resolved/Reopened, `closeReason` for Closed,
    `duplicateOfId` for Duplicate) (API → **422**).
  Exception → status mapping is added to `ProblemDetailsMiddleware` in this part (Api touch only; see §4).

`Incidents/`
- Enums, member names identical to API-CONTRACT §2 (stored as strings in 04_02):
  `Severity { Critical, High, Medium, Low }`, `Priority { P1, P2, P3, P4 }`,
  `IncidentStatus { New, Triaged, InProgress, OnHold, Resolved, Reopened, Closed }`,
  `IncidentSource { Manual, Webhook }` (see OQ 1), `CloseReason { Fixed, Duplicate, Invalid, CannotReproduce }` (see OQ 2).
  `Environment` reuses the existing `Apps/AppEnvironment`.
- `IncidentNumber` — value object over `int`; `ToString()` → `"INC-1042"`; `TryParse("INC-1042")`.
- `IncidentFingerprint.Compute(Guid applicationId, string title)` — lower-case, trim, collapse whitespace, strip punctuation,
  then SHA-256 hex of `"{applicationId:N}|{normalized}"` (64 chars, fits `nvarchar(300)`). Deterministic and culture-invariant.
- `IncidentWorkflow` (static) — single source of truth for the table in CLAUDE.md:

  | From | To | Who |
  |---|---|---|
  | New | Triaged; Closed (`Duplicate`/`Invalid` only) | Responder |
  | Triaged | InProgress, OnHold | Responder |
  | InProgress | Resolved, OnHold | Assignee, Responder |
  | OnHold | InProgress | Assignee, Responder |
  | Resolved | Closed, Reopened | Reporter, Responder |
  | Reopened | InProgress | Responder |

  API: `bool IsDefined(from, to)`, `bool IsAllowed(from, to, Actor, Incident)`,
  `IReadOnlyList<IncidentStatus> AllowedTransitions(Incident, Actor)` (BE-07 uses this for `allowedTransitions`).
  "Responder" = actor has role `Responder` (TeamLead/Admin per **OQ 3**); "Assignee" = `actor.UserId == incident.AssigneeId`;
  "Reporter" = `actor.UserId == incident.ReporterId` (per **OQ 4**). `Actor.System` is never allowed a user transition.
  `Closed` is terminal (no outgoing row).
- `Incident : AggregateRoot` — all setters private; private parameterless ctor for EF.
  Properties per DATABASE.md `Incidents` + `DuplicateOfId` (OQ 6) + `UpdatedAt` (OQ 5):
  `Number` (int, `0` until the DB assigns it — see 04_02), `ApplicationId`, `Environment`, `Title`, `Description`, `StepsToReproduce?`,
  `Severity`, `Priority?`, `Status`, `Source`, `ReporterId`, `AssigneeId?`, `AssignedTeamId?`, `Fingerprint?`, `ExternalId?`,
  `AckDueAt?`, `ResolveDueAt?`, `IsSlaBreached`, `AcknowledgedAt?`, `ResolvedAt?`, `ClosedAt?`, `CloseReason?`, `DuplicateOfId?`,
  `CreatedAt`, `UpdatedAt`, `RowVersion` (`byte[]`), `IReadOnlyList<IncidentHistory> History`.
  Length constants: `TitleMaxLength = 200` (OQ 7), `DescriptionMaxLength = 20_000`, `StepsMaxLength = 5_000`, `NoteMaxLength = 1_000`.
- Methods (time is passed in — the domain cannot inject `TimeProvider`; see **OQ 8**):
  - `static Incident Report(Guid applicationId, AppEnvironment env, Severity severity, string title, string description,
    string? steps, IncidentSource source, Actor reporter, DateTimeOffset at, string? externalId = null)` →
    `Status = New`, `Fingerprint` computed, history row `Status: null → New`, raises `IncidentReported`.
    SLA due times are left null here; `SetSlaDueTimes` sets them (see below).
  - `void TransitionTo(IncidentStatus to, Actor actor, string? note, DateTimeOffset at, CloseReason? closeReason = null, Guid? duplicateOfId = null)`
    1. not defined → `InvalidTransitionException`; 2. not allowed for actor → `TransitionNotPermittedException`;
    3. requirements (note required for Resolved/Reopened; `closeReason` required for Closed and must be Duplicate/Invalid from New;
       `duplicateOfId` required iff `Duplicate`, ≠ own `Id`) → `TransitionRequirementException`.
    Side effects: `AcknowledgedAt` set on first move out of `New` (OQ 9); `ResolvedAt` set on Resolved; `ClosedAt`/`CloseReason`/`DuplicateOfId`
    set on Closed; on Reopened, `ResolvedAt` stays (history keeps the first resolution; OQ 9). Appends history `Status: old → new` with `note`
    (plus `CloseReason` row when closing). Raises `IncidentTransitioned(IncidentId, From, To, ActorId, Note)`. Updates `UpdatedAt`.
  - `void Assign(Guid? assigneeId, Guid? teamId, Actor actor, DateTimeOffset at)` — history row per changed field (`AssigneeId`, `AssignedTeamId`);
    no-op (no row, no event) when nothing changes; raises `IncidentAssigned`. Role check is the `CanAssignIncident` policy (Api), not the domain.
    Not allowed when `Closed` → `InvalidTransitionException`-style `IncidentClosedException` (409).
  - `void Update(IncidentChanges changes, Actor actor, DateTimeOffset at)` — title/description/steps/priority/application/environment; one history
    row per changed field; raises one `IncidentUpdated(IncidentId, ChangedFields)`; recomputes `Fingerprint` when title/app change. Rejected when `Closed`.
  - `void ChangeSeverity(Severity severity, string reason, DateTimeOffset ackDueAt, DateTimeOffset resolveDueAt, Actor actor, DateTimeOffset at)` —
    `reason` required; history row with `reason` as note; due times are computed by `SlaClock` in the handler and passed in (CLAUDE.md: never in controllers).
    Raises `IncidentUpdated`.
  - `void SetSlaDueTimes(DateTimeOffset ackDueAt, DateTimeOffset resolveDueAt)` — used at creation (BE-05).
  - `void MarkSlaBreached(DateTimeOffset at)` — idempotent; history row with `Actor.System` (consumed by BE-12).
- `IncidentHistory : Entity` — `IncidentId`, `Field` (≤50), `OldValue?`, `NewValue?`, `Note?` (≤1000), `ActorId`, `CreatedAt`.
  `internal` factory; no public setters, no update methods (append-only). Enum values written by name, Guids as `D` strings, times as ISO 8601 UTC.
- `Events/`: `IncidentReported`, `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated` — sealed records carrying Ids and changed values only
  (no aggregate references), so 04_02/BE-05 can serialize them into outbox payloads.

### Application / Infrastructure / Worker
None in this part.

### Api
- `Errors/ProblemDetailsMiddleware`: map `InvalidTransitionException` and `IncidentClosedException` → 409 (`type` `https://incidenthub/errors/invalid-transition`),
  `TransitionNotPermittedException` → 403, `TransitionRequirementException` → 422 with `errors` keyed by request field (`note`, `closeReason`, `duplicateOfId`).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `contracts/swagger.json` unchanged, except the doc fixes listed under Open questions once decided
(enum names, `CloseReason`, 403-vs-409 wording in §6).

## 5. Acceptance criteria

- [ ] Every row of the transition table has a passing unit test per allowed "who" (Responder, Assignee, Reporter as applicable).
- [ ] Every `(from, to)` pair **not** in the table (all 7×7 combinations, incl. same-status and anything out of `Closed`) throws `InvalidTransitionException`.
- [ ] Every allowed `(from, to)` attempted by a non-permitted actor throws `TransitionNotPermittedException` (e.g. Reporter role triaging; non-assignee, non-responder resolving).
- [ ] `Incident.Status` (and every other property) has no public setter — enforced by a reflection test over `Incident` and `IncidentHistory`.
- [ ] Each successful transition appends exactly one `Status` history row with old value, new value, actor id, timestamp and note.
- [ ] `Report` produces `Status = New`, one history row, one `IncidentReported` event; `Assign`/`Update`/`ChangeSeverity` produce one row per changed field and their event; no-ops produce neither.
- [ ] Closing requires a valid `closeReason` (Duplicate/Invalid from New), Duplicate requires `duplicateOfId`; Resolved/Reopened require a note.
- [ ] `AllowedTransitions(incident, actor)` equals the table for each role/relation.
- [ ] `IncidentFingerprint.Compute` is stable across casing/whitespace/punctuation differences and differs across applications.
- [ ] Domain project still has no package or project references; `dotnet build` (warnings as errors) and `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Incidents/` (pure xUnit, FluentAssertions, no traits):
- `IncidentWorkflowTests`
  - `TransitionTo_AllowedTransitionForPermittedActor_ChangesStatus` — `[Theory]` over every table row × permitted actor kind.
  - `TransitionTo_UndefinedTransition_ThrowsInvalidTransition` — `[Theory]` generated from all 49 pairs minus the table.
  - `TransitionTo_ActorNotPermitted_ThrowsTransitionNotPermitted` — `[Theory]` per row × non-permitted actor (Reporter role, other user, `Actor.System`).
  - `AllowedTransitions_ForEachRoleAndStatus_MatchesTable`.
- `IncidentTests`
  - `Report_ValidInput_StartsNewWithHistoryAndEvent`, `Report_TitleTooLong_Throws`.
  - `TransitionTo_Valid_AppendsStatusHistoryRow`, `TransitionTo_ResolvedWithoutNote_ThrowsRequirement`, `TransitionTo_ReopenedWithoutNote_ThrowsRequirement`,
    `TransitionTo_ClosedFromNewWithFixed_ThrowsRequirement`, `TransitionTo_DuplicateWithoutDuplicateOfId_ThrowsRequirement`,
    `TransitionTo_DuplicateOfSelf_ThrowsRequirement`, `TransitionTo_FirstLeaveNew_SetsAcknowledgedAt`, `TransitionTo_Resolved_SetsResolvedAt`, `TransitionTo_Closed_SetsClosedAtAndReason`.
  - `Assign_NewAssignee_AppendsHistoryAndRaisesEvent`, `Assign_SameAssignee_NoHistoryNoEvent`, `Assign_ClosedIncident_Throws`.
  - `Update_ChangedTitle_RecomputesFingerprintAndAppendsHistory`, `ChangeSeverity_WithoutReason_Throws`, `ChangeSeverity_Valid_SetsDueTimesAndAppendsHistory`.
  - `Properties_HaveNoPublicSetters` (reflection over `Incident`, `IncidentHistory`).
- `IncidentFingerprintTests`: `Compute_SameTitleDifferentCasingAndSpacing_Equal`, `Compute_DifferentApplication_Differs`.
- `IncidentNumberTests`: `ToString_Formats_IncPrefix`, `TryParse_InvalidInput_ReturnsFalse`.

`tests/IncidentHub.Api.UnitTests/Errors/ProblemDetailsMiddlewareTests`: `InvokeAsync_InvalidTransition_Returns409`,
`InvokeAsync_TransitionNotPermitted_Returns403`, `InvokeAsync_TransitionRequirement_Returns422WithFieldErrors`.

## 7. Out of scope

- EF configuration, SQL `SEQUENCE`, indexes, full-text, migration, fingerprint-conflict mapping → 04_02.
- `SlaClock` / default SLA policy and computing `AckDueAt`/`ResolveDueAt` at creation → BE-05 (configurable `SlaPolicy` → BE-12).
- `OutboxMessage` entity and converting domain events into outbox rows in the same `SaveChangesAsync` → BE-05.
- Post-commit SignalR dispatch of domain events → BE-09.
- Comments, attachments and their events (`CommentAdded`, `AttachmentAdded`) → BE-08.
- `AppStatusCalculator` → BE-11.
- Team-membership resource check on incident actions (Api `AuthorizeAppAsync`) → BE-05/BE-07 endpoints.

## 8. Open questions — resolved

All decided with the user on 2026-10-11:

1. `IncidentSource` = `Manual | Webhook` (API-CONTRACT wins; DATABASE.md fixed).
2. `CloseReason` = `Fixed | Duplicate | Invalid | CannotReproduce` (DATABASE.md fixed). From `New` only Duplicate/Invalid; from `Resolved` any except Duplicate.
3. TeamLead and Admin act as Responder in every "Responder" cell (Admin still needs team membership — an Api check).
4. "Reporter"/"Assignee" are relations (`ReporterId`/`AssigneeId` equal the actor), not roles.
5. `UpdatedAt` added to the aggregate and DATABASE.md.
6. `DuplicateOfId` (nullable self-FK) added to the aggregate and DATABASE.md.
7. Domain title max 200; the column stays `nvarchar(300)` (04_02).
8. Signature `TransitionTo(to, actor, note, at, closeReason?, duplicateOfId?)`; CLAUDE.md updated.
9. `AcknowledgedAt` = first move out of `New`, never reset; Reopen keeps `ResolvedAt`, the next Resolved overwrites it.
10. Not in the table → 409; listed but not permitted → 403; missing input → 422. API-CONTRACT §6 updated.
11. `IncidentHistory.ActorId` is nullable; `null` = system (DATABASE.md updated; the 04_02 migration makes the column nullable).
12. History `Field` stores property names (`AssigneeId`, not `Assignee`).
13. Typed `DomainException`s, no `Result<T>`.

Also decided: `Assign`/`Update`/`ChangeSeverity` reject only `Closed` incidents (Resolved stays editable); `SetSlaDueTimes` and `MarkSlaBreached` are built now.

## 9. Implementation notes

- Added beyond the spec text: `Optional<T>` (`Domain/Common`) so `IncidentChanges` can tell "leave as is" from "set to null"; `TransitionTo` also rejects a `closeReason` when the target is not `Closed`, and a `duplicateOfId` unless the reason is `Duplicate`; closing as Duplicate also writes a `DuplicateOfId` history row.
- `ChangeSeverity` to the same severity is a no-op (no history, no event, due times untouched).
- `MarkSlaBreached` raises `IncidentUpdated` so that every change raises an event.
