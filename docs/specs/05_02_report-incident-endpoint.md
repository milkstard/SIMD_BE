# Spec 05_02: Report incident endpoint

> Status: **Draft** · Source: BE-05 in docs/BACKEND-SPECS.md · Part 02 of 02

## 1. Purpose & scope

The first user-facing write path: `POST /api/v1/incidents`. A team member reports an incident against one of their
applications; in **one** `SaveChangesAsync` the incident row, its first `IncidentHistory` row and its `IncidentReported`
outbox row (05_01) are written, with `AckDueAt`/`ResolveDueAt` already set. The endpoint honours `Idempotency-Key`
(same key + same body → same result, no duplicate row).

In scope:
- `ReportIncident` use case (command + handler + validator).
- HTML sanitization of rich text (`IHtmlSanitizer`).
- `IncidentSummaryDto` / `IncidentDetailDto` and their mapping (the 201 body; reused by BE-07).
- `IncidentsController.Report`, `Location` + `ETag` headers.
- Idempotency body-hash check (applies to every `[Idempotent]` endpoint).
- Duplicate-fingerprint pre-check that reports the existing incident number.

Pulled in from `deferred-items.md`: "Team-membership resource check on incident endpoints" — the **BE-05 half** (report).
The BE-07 half stays deferred.

## 2. Dependencies

- BE-02 (`ICurrentUser`, `Policies.CanReportIncident`, `IAppTeamReader`), BE-03 (`MonitoredApp`, `IdempotencyFilter`, `ETagResultFilter`, `IVersionedDto`, `Refs`).
- BE-04 (`Incident.Report`, `IncidentWorkflow.AllowedTransitions`, `IncidentNumber`, `DuplicateIncidentException`, `IX_Incidents_Fingerprint_Open`).
- **05_01** (`SlaClock`, `ISlaPolicyProvider`, outbox written in `SaveChangesAsync`).

## 3. Design

### Application (`IncidentHub.Application`)
`Abstractions/IHtmlSanitizer` — `string Sanitize(string html)`.

`Incidents/ReportIncident/`
- `ReportIncidentCommand` — sealed record `(Guid ApplicationId, AppEnvironment Environment, Severity Severity, string Title,
  string Description, string? StepsToReproduce, IReadOnlyList<Guid> AttachmentIds) : IRequest<IncidentDetailDto>`.
- `ReportIncidentValidator` (no data access):
  - `applicationId` not empty; `environment`, `severity` defined enum values.
  - `title` required, trimmed length 5–200 (`Incident.TitleMaxLength`).
  - `description` required, ≤ 20,000 (`Incident.DescriptionMaxLength`); `stepsToReproduce` ≤ 5,000.
  - `attachmentIds` must be empty until BE-08: "Attachments cannot be linked yet." (OQ 4). Max 10 rule is added with BE-08.
- `ReportIncidentHandler(IAppDbContext db, ICurrentUser currentUser, IAppTeamReader teams, IHtmlSanitizer sanitizer,
  ISlaPolicyProvider slaPolicies, TimeProvider time, ILogger<ReportIncidentHandler> logger)`:
  1. Load the application (`AsNoTracking`, `Select` of `IsActive`, `Environments`). Missing, inactive, **or caller not a member**
     (`teams.IsMemberAsync(currentUser.UserId, applicationId)`, no Admin bypass — OQ 5) → `ValidationException` on `applicationId`
     ("Application not found or not available to you."), same message for all three so existence is not revealed.
  2. `environment` not in the application's `Environments` → `ValidationException` on `environment`.
  3. Sanitize `description` and `stepsToReproduce`; title is plain text (trimmed, not sanitized — rendered as text by the SPA).
     Description empty/whitespace after sanitizing → `ValidationException` on `description`.
  4. Duplicate pre-check (OQ 6): open incident (`Status != Closed`) with `Fingerprint == IncidentFingerprint.Compute(app, title)` →
     `DuplicateIncidentException(existingNumber)`.
  5. `Incident.Report(..., IncidentSource.Manual, currentUser.ToActor(), time.GetUtcNow())`;
     `incident.SetSlaDueTimes(SlaClock.DueTimes(await slaPolicies.GetAsync(severity), incident.CreatedAt))`.
  6. `db.Incidents.Add(incident)`; one `SaveChangesAsync(ct)` → incident + history + outbox (05_01). A race on the fingerprint index
     still surfaces as `DuplicateIncidentException` (no number) from `AppDbContext` (04_02).
  7. Log `"Incident {IncidentNumber} reported for application {ApplicationId} with severity {Severity}"`.
  8. Return `IncidentDetailDto` (see below).

`Common/Exceptions/DuplicateIncidentException` — add optional `string? ExistingIncidentNumber` (ctor overload); message unchanged.

`Incidents/Dtos/`
- `IncidentSummaryDto` and `IncidentDetailDto` — records matching API-CONTRACT §4 field-for-field (`number` as `"INC-1042"`,
  `rowVersion` base64, implements `IVersionedDto` so `ETagResultFilter` adds the `ETag`). `AttachmentDto` record per contract.
- `IncidentQueries.GetDetailAsync(IAppDbContext db, Incident incident, Actor actor, CancellationToken ct)`:
  one `AsNoTracking` projection for the refs (application, reporter, assignee, assigned team) by `incident.Id`, combined with
  values from the saved aggregate; `allowedTransitions` = `IncidentWorkflow.AllowedTransitions(incident, actor)`;
  `canEdit` = Responder-level role or reporter while not Closed; `canAssign` = Responder-level role while not Closed (OQ 7);
  `commentCount` = 0 and `attachments` = [] until BE-08; `duplicateOf` = null on creation.
  BE-07 reuses the DTOs and adds its own by-number query.

### Infrastructure
- `Security/HtmlSanitizerAdapter : IHtmlSanitizer` over the existing `HtmlSanitizer` package (already referenced): default allow-list,
  strips `<script>`, event-handler attributes and `javascript:` URLs. Singleton; registered in `AddInfrastructure`.
- `Caching/RedisIdempotencyStore` + `IIdempotencyStore`: `IdempotentResponse` gains `string RequestHash`; `TryBeginAsync` stores the hash in the
  pending marker so a concurrent request with a different body can be told apart (OQ 3).

### Api
- `Contracts/IncidentRequests.cs` — `ReportIncidentRequest` (nullable members, mapped to the command like `CreateApplicationRequest`).
- `Controllers/IncidentsController` (`[Route("api/v1/incidents")]`, thin):
  `[HttpPost] [Authorize(Policy = Policies.CanReportIncident)] [Idempotent]` → `ISender.Send` →
  `Created($"/api/v1/incidents/{dto.Number}", dto)`. `ProducesResponseType` for 201/400/401/403/409/422.
- `Filters/IdempotencyFilter`: compute SHA-256 (hex) of the buffered request body (`EnableBuffering`, read, rewind);
  completed or pending entry with a different hash → **422** `title` "Idempotency-Key reused" ("…was used with a different request body.").
  Same hash → existing behaviour (replay / 409 while in progress). Applies to Teams and Applications creates too.
- `Errors/ProblemDetailsMiddleware`: when a `DuplicateIncidentException` carries a number, add extension `existingIncidentNumber`
  to the 409 (`title` "Duplicate incident"). The generic `ConflictException` branch is otherwise unchanged.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| POST | `/api/v1/incidents` | `ReportIncidentRequest` | `201` `IncidentDetail`, `Location: /api/v1/incidents/INC-{n}`, `ETag` | 400 malformed body / `Idempotency-Key`; 401; 403 (policy); 409 duplicate open incident or same key in progress; 422 validation, non-member/unknown/inactive application, environment not offered, non-empty `attachmentIds`, key reused with another body |

`ReportIncidentRequest`, `IncidentSummary`, `IncidentDetail` already exist in API-CONTRACT §4/§5 — no shape change.
Update `API-CONTRACT.md` in the same change (additive):
- §5 Incidents: note that `attachmentIds` must be empty until BE-08 (422), and that reporting requires membership of the application's team.
- §6: 409 for a duplicate open incident carries `existingIncidentNumber`; 422 when an `Idempotency-Key` is reused with a different body.

Regenerate `contracts/swagger.json` with `scripts/generate-openapi.ps1` (new path + DTO schemas).

## 5. Acceptance criteria

- [ ] Invalid payload (missing/short title, unknown enum, oversized description, non-empty `attachmentIds`) → `422` with camelCase field keys in `ProblemDetails.errors`; malformed JSON → `400`.
- [ ] Valid request → `201`, `Location: /api/v1/incidents/INC-{n}`, `ETag` equal to `"<rowVersion>"`, body is an `IncidentDetail` with `status = New`, `source = Manual`, the caller as `reporter`, `allowedTransitions` per the workflow table for the caller.
- [ ] `AckDueAt`/`ResolveDueAt` are non-null and equal `createdAt + policy minutes` for the incident's severity in the 201 body and in the database.
- [ ] Incident row, one `Status: null → New` history row and one `IncidentReported` outbox row are written by one `SaveChangesAsync` (end-to-end check; the rollback proof is in 05_01).
- [ ] Two requests with the same `Idempotency-Key` and body (sequential, and concurrent) produce exactly one incident; the repeat gets the original `201` body and `Location`.
- [ ] Same `Idempotency-Key` with a different body → `422`, no new incident.
- [ ] A `<script>` / `onerror=` payload in `description` or `stepsToReproduce` is stripped before storage; description that is empty after sanitizing → `422`.
- [ ] Caller not on the application's team (Admin included), unknown or inactive application → `422` on `applicationId`, identical message, no row written.
- [ ] Environment not in the application's `Environments` → `422` on `environment`.
- [ ] Second open incident with the same app + normalized title → `409` with `existingIncidentNumber`; after the first is closed, reporting succeeds.
- [ ] No token → `401`; token without a role → `403`.
- [ ] Controller has no logic beyond mapping; no `DateTime.UtcNow`; `API-CONTRACT.md` and `swagger.json` updated; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Incidents/ReportIncident/`
- `ReportIncidentValidatorTests` (`TestValidate`): `Validate_ValidCommand_NoErrors`, `Validate_TitleTooShort_HasTitleError`, `Validate_TitleTooLong_HasTitleError`,
  `Validate_DescriptionTooLong_HasDescriptionError`, `Validate_UndefinedSeverity_HasSeverityError`, `Validate_AttachmentIdsNotEmpty_HasAttachmentIdsError`.
- `ReportIncidentHandlerTests` (NSubstitute for `IAppTeamReader`, `IHtmlSanitizer`, `ISlaPolicyProvider`; `FakeTimeProvider`; SQL container `IAppDbContext` per CLAUDE.md
  "don't mock DbContext" — tag `[Trait("Category","Integration")]` where the DB is used):
  `Handle_NotMember_ThrowsValidationOnApplicationId`, `Handle_InactiveApplication_ThrowsValidationOnApplicationId`, `Handle_EnvironmentNotOffered_ThrowsValidationOnEnvironment`,
  `Handle_DescriptionEmptyAfterSanitize_ThrowsValidation`, `Handle_OpenDuplicate_ThrowsDuplicateWithNumber`, `Handle_Valid_SetsSlaDueTimesFromPolicy`.

`tests/IncidentHub.Api.UnitTests/`
- `Filters/IdempotencyFilterTests`: `OnActionExecutionAsync_SameKeyDifferentBody_Returns422`, `OnActionExecutionAsync_SameKeySameBody_ReplaysResponse`.
- `Errors/ProblemDetailsMiddlewareTests`: `InvokeAsync_DuplicateIncidentWithNumber_Returns409WithExistingNumber`.
- `HtmlSanitizerAdapterTests` (wherever Infrastructure unit tests live): `Sanitize_ScriptTag_Removed`, `Sanitize_OnErrorAttribute_Removed`, `Sanitize_SafeMarkup_Kept`.

`tests/IncidentHub.Api.IntegrationTests/Incidents/ReportIncidentTests` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `Report_Valid_Returns201WithLocationETagAndDetail`
- `Report_Valid_SetsSlaDueTimesForSeverity` (`[Theory]` per severity)
- `Report_Valid_WritesIncidentHistoryAndOutboxRows`
- `Report_InvalidPayload_Returns422WithFieldErrors`, `Report_MalformedJson_Returns400`
- `Report_SameIdempotencyKeyTwice_CreatesOneIncident`, `Report_SameIdempotencyKeyConcurrent_CreatesOneIncident`, `Report_SameKeyDifferentBody_Returns422`
- `Report_ScriptInDescription_StoredSanitized`
- `Report_NonMember_Returns422`, `Report_AdminNotMember_Returns422`, `Report_UnknownApplication_Returns422`, `Report_EnvironmentNotOffered_Returns422`
- `Report_OpenDuplicate_Returns409WithExistingNumber`, `Report_DuplicateOfClosed_Returns201`
- `Report_NoToken_Returns401`, `Report_NoRole_Returns403`
- Existing Teams/Applications idempotency tests still pass with the body-hash check.

## 7. Out of scope

- `GET /incidents/{number}`, list, `PATCH`, transitions, assignment and their team-membership checks → BE-07.
- Linking `attachmentIds`, the max-10 rule, real `commentCount`/`attachments` in `IncidentDetail` → BE-08.
- `IncidentCreated` / `DashboardChanged` SignalR broadcasts after the report → BE-09.
- Emailing the owning team for `IncidentReported` → BE-10.
- Webhook-sourced incidents (`Source = Webhook`, `externalId`) → BE-17.
- Similar-incident hints while typing → BE-06.

## 8. Open questions

Decided with the user on 2026-10-12:

3. Same `Idempotency-Key` + different body → `422`; the store keeps a SHA-256 of the body. Applies to every `[Idempotent]` endpoint.
4. `attachmentIds` accepted by the contract but a non-empty list → `422` until BE-08.
5. Reporting requires membership of the application's owning team (`AppTeamRequirement.Member` semantics, no Admin bypass); non-member, unknown and inactive applications all → `422` on `applicationId` (no existence leak).
6. Duplicate open incident: handler pre-check → `409` with `existingIncidentNumber`; the unique index stays the race backstop (generic 409 without the number).

(OQ 1–2 are in 05_01.)

Still open (confirm during implementation):

7. No doc defines `canEdit`/`canAssign`. Proposed: `canAssign` = Responder/TeamLead/Admin and not Closed (matches `CanAssignIncident`); `canEdit` = same roles, or the reporter, while not Closed. BE-07 owns the PATCH rules and may refine this.
8. CLAUDE.md asks for 400/422 on validation; the existing middleware returns 422 for all `ValidationException`s and 400 only for malformed JSON/headers. This spec keeps that split.
9. Cross-cutting "Rate limiting on `POST /incidents`" (BACKEND-SPECS → Security) is not owned by any BE item. Not deferred here; decide whether it joins this part or gets its own item.
10. BE-05 says "400/422 with field-level errors"; API-CONTRACT §6 has 413 only for files, so an oversized JSON body is treated as 422 (length rules), not 413.
