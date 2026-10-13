# Spec 08_01: Comments & history endpoints

> Status: **Implemented** · Source: BE-08 in docs/BACKEND-SPECS.md · Part 01 of 03

## 1. Purpose & scope

The collaboration read/write surface on an incident that needs no file storage:
- `GET /api/v1/incidents/{id}/comments` — paged, oldest first; internal comments never reach Reporters.
- `POST /api/v1/incidents/{id}/comments` — add a (sanitized) comment; appends a history row and an outbox row.
- `GET /api/v1/incidents/{id}/history` — paged, newest first.

In scope:
- `Comment` entity, `Comments` table + migration (`AddComments`), `IAppDbContext.Comments`.
- `Incident.AddComment(...)`-style domain behaviour that appends the history row and raises `CommentAdded` (04_01 deferred it to BE-08).
- Real `commentCount` in `IncidentSummary`/`IncidentDetail` (0 until now).
- Replacing the stub `CommentDto` with the contract shape (author, `attachments: []`, `editedAt`).
- Comment/history endpoints, `Idempotency-Key` on `POST` comments.

Pulled in from `deferred-items.md`:
- "`Comment`/`Attachment` entities, tables and events" — the **Comment** half (`CommentAdded`); the Attachment half is 08_02.
- "Real `commentCount` and `attachments` in `IncidentDetail`" — `commentCount` here; `attachments` in 08_03.

## 2. Dependencies

- BE-04 (`Incident` aggregate, `IncidentHistory`, domain events), 05_01 (outbox written in `SaveChangesAsync`), 05_02 (`IHtmlSanitizer`, `IdempotencyFilter`, DTO mappers).
- 07_01/07_02 (`IncidentWriteGuard`, visibility rules: non-member and unknown id are both `404`).
- BE-02 (`Policies.CanViewIncident`, `Policies.CanCommentInternally`, `ICurrentUser`).

## 3. Design

### Domain
- `Incidents/Comment` (entity): `IncidentId`, `AuthorId`, `Body` (sanitized HTML, ≤ `Comment.BodyMaxLength` = 10,000), `IsInternal`, `CreatedAt`, `EditedAt` (null; no edit endpoint in v1). Created only through the aggregate.
- `Incident.AddComment(string sanitizedBody, bool isInternal, Actor actor, DateTimeOffset at)` → returns the `Comment`; requires an actor that is a user (not `Actor.System`); allowed on a closed incident (OQ 3); appends one `IncidentHistory` row (OQ 1) and raises `CommentAdded(IncidentId, CommentId, IsInternal, ActorId, At)`.
- `isInternal = true` from a non-responder actor → `TransitionNotPermittedException`-style 403 (new `CommentNotPermittedException`, mapped in `ProblemDetailsMiddleware`, title "Internal comment not permitted").
- `CommentAdded` is a domain event mapped to an outbox row by the generic `AppDbContext.StageOutbox` (no new mapper code). The row payload carries ids only, never the body.

### Application
`Incidents/AddComment/`
- `AddCommentCommand(Guid IncidentId, string Body, bool IsInternal) : IRequest<CommentDto>` (`AttachmentIds` is added by 08_03).
- `AddCommentValidator` (no data access): `body` required, ≤ 10,000, not empty after trim.
- `AddCommentHandler(IAppDbContext db, ICurrentUser currentUser, IncidentWriteGuard guard, IHtmlSanitizer sanitizer, TimeProvider time, ILogger<...>)`:
  1. Load the incident tracked (with history) via the guard → missing or non-member → `NotFoundException` (404).
  2. Sanitize `body`; empty after sanitizing → `ValidationException` on `body`.
  3. `incident.AddComment(...)`; `db.Comments.Add(comment)`.
  4. One `SaveChangesAsync` → comment + history + outbox.
  5. Log `"Comment added to incident {IncidentNumber}"` (never the body).
  6. Return the `CommentDto` built from the saved entity plus the caller's `UserRefDto` (no re-query).

`Incidents/ListComments/`
- `ListCommentsQuery(Guid IncidentId, string? Cursor, int Limit) : IRequest<Paged<CommentDto>>`; validator: `limit` 1–100 (default 25), cursor parseable (`422`).
- Handler: visibility check (404 for non-member/unknown); `AsNoTracking()` + `Select` into `CommentDto`; ordered `(CreatedAt, Id)` ascending with a keyset cursor (same cursor scheme as 07_01); **`WHERE IsInternal = 0` unless the caller `IncidentPermissions.IsResponder`**. The filter is applied in the query so internal rows are never materialized for Reporters. `totalCount` only with `includeTotal=true`.

`Incidents/ListHistory/`
- `ListHistoryQuery(Guid IncidentId, string? Cursor, int Limit)`; same paging rules, ordered `(CreatedAt, Id)` descending using `IX_IncidentHistory_Incident_CreatedAt`.
- Projection to `HistoryEntryDto(Id, Actor: UserRefDto?, Field, OldValue, NewValue, Note, At)`. `Field` is mapped to a display name (`AssigneeId` → "Assignee", `AssignedTeamId` → "Team", `StatusChanged`…) by one static map `HistoryFieldNames`; unknown fields pass through unchanged.
- `OldValue`/`NewValue` for id-valued fields (`AssigneeId`, `AssignedTeamId`, `DuplicateOfId`, `ApplicationId`) are resolved to display names / incident numbers in one batched lookup per page (OQ 2).

### Infrastructure
- `Persistence/Configurations/CommentConfiguration` (no data annotations): table `Comments`, `Body nvarchar(max)`, `IsInternal bit default 0`, FKs to `Incidents` and `Users` (`Restrict`), index `IX_Comments_Incident_CreatedAt` on `(IncidentId, CreatedAt)`.
- Migration `AddComments` (`dotnet ef migrations add AddComments -p src/IncidentHub.Infrastructure -s src/IncidentHub.Api`).
- `commentCount`: computed in the list/detail projections (`IncidentProjections`) with a correlated count that excludes internal comments for non-responders (OQ 4). List queries must stay index-backed; verify the plan test from 07_01 still passes.

### Api
`Controllers/IncidentsController`:
- `[HttpGet("{id:guid}/comments")] [Authorize(Policy = Policies.CanViewIncident)]`.
- `[HttpPost("{id:guid}/comments")] [Authorize(Policy = Policies.CanViewIncident)] [Idempotent]` → `201` with `Location` of the list and the `Comment` body. Internal visibility is decided in the handler (a Reporter sending `isInternal: true` gets `403`; `Policies.CanCommentInternally` is not applied to the whole action).
- `[HttpGet("{id:guid}/history")] [Authorize(Policy = Policies.CanViewIncident)]`.
- `Contracts/AddCommentRequest(Body, IsInternal)`; controller only maps.
- Request-size cap on `POST comments` (128 KiB, decision 7), consistent with 05_02's idempotency body cap. The body length is checked again after sanitizing (a literal `<` becomes `&lt;`) and answers `422` on `body`.

### Worker
None.

## 4. Endpoints & DTOs

| Method | Path | Body | Success | Errors |
|---|---|---|---|---|
| GET | `/api/v1/incidents/{id}/comments?cursor=&limit=25` | — | `200` `Paged<Comment>` (oldest first) | 401; 404 unknown or not visible; 422 bad `limit`/`cursor` |
| POST | `/api/v1/incidents/{id}/comments` | `AddCommentRequest` | `201` `Comment` | 400 malformed; 401; 403 `isInternal: true` by a non-responder; 404; 413 over the body cap; 422 empty/too long body, reused `Idempotency-Key` with another body |
| GET | `/api/v1/incidents/{id}/history?cursor=&limit=25` | — | `200` `Paged<HistoryEntry>` (newest first) | 401; 404; 422 |

`Comment`, `HistoryEntry`, `AddCommentRequest` exist in API-CONTRACT §4/§5. Update `API-CONTRACT.md` with: 404 for non-visible incidents, `403` for internal comments by non-responders, Reporters never see internal comments or the `commentCount` they contribute to, the paging `limit` rule, the `Field` display names, and `attachments: []` until 08_03. Regenerate `contracts/swagger.json` (`scripts/generate-openapi.ps1`).

## 5. Acceptance criteria

- [ ] A `Reporter` calling the comments list never receives a comment with `isInternal = true` — verified at API level, including `commentCount` and the history response.
- [ ] A body that is within 10,000 characters raw but longer once sanitized is `422` on `body`, never a `500`.
- [ ] A comment body containing `<script>` is stripped before storage (assert on the persisted row, not only the response).
- [ ] A Reporter posting `isInternal: true` → `403`; a Responder/TeamLead/Admin may.
- [ ] Posting a comment writes the `Comments` row, one `IncidentHistory` row and one `CommentAdded` outbox row in a single `SaveChangesAsync` (rollback verified).
- [ ] Two requests with the same `Idempotency-Key` and body produce exactly one comment.
- [ ] Comments list is oldest first, stable across pages with identical `CreatedAt` (keyset tie-break); history is newest first; both reject a bad `cursor`/`limit` with `422`.
- [ ] A caller outside the application's team gets `404` on all three endpoints; the id of an unknown incident gives the same `404`.
- [ ] `commentCount` in `IncidentSummary`/`IncidentDetail` reflects the comments the caller may see.
- [ ] Controller maps only; handlers use `TimeProvider`; list queries use `AsNoTracking()` + `Select`; `API-CONTRACT.md` and `swagger.json` updated; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Incidents/`
- `IncidentCommentTests`: `AddComment_Responder_AppendsHistoryAndRaisesEvent`, `AddComment_InternalByReporter_Throws`, `AddComment_SystemActor_Throws`, `AddComment_ClosedIncident_Succeeds`.

`tests/IncidentHub.Application.UnitTests/Incidents/`
- `AddCommentValidatorTests`: `Validate_EmptyBody_HasError`, `Validate_BodyTooLong_HasError`, `Validate_ValidBody_NoErrors`.
- `ListCommentsValidatorTests` / `ListHistoryValidatorTests`: `Validate_LimitOutOfRange_HasError`, `Validate_BadCursor_HasError`.
- `HistoryFieldNamesTests`: known fields map to display names, unknown pass through.

`tests/IncidentHub.Api.IntegrationTests/Incidents/` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `CommentTests`: `List_AsReporter_HidesInternalComments`, `List_AsResponder_ShowsInternalComments`, `Add_ScriptInBody_IsStripped`, `Add_InternalAsReporter_Returns403`, `Add_WritesCommentHistoryAndOutboxInOneSave`, `Add_SameIdempotencyKey_CreatesOne`, `Add_NonMember_Returns404`, `Add_UnknownIncident_Returns404`, `Add_OnClosedIncident_Returns201`, `List_Paging_IsStableWithEqualTimestamps`, `List_NoToken_Returns401`, `CommentCount_ExcludesInternalForReporter`.
- `HistoryTests`: `History_NewestFirst_Paged`, `History_AfterComment_ContainsCommentRow`, `History_InternalComment_NotVisibleToReporter` (per OQ 1), `History_NonMember_Returns404`, `History_ResolvesAssigneeName`.
- Rollback test: a failing save (real concurrent writer, as in 07_02) leaves comment, history and outbox unchanged.
- Migration/plan test: comments list for a 50k-comment incident uses the `(IncidentId, CreatedAt)` index.

## 7. Out of scope

- Comment edit/delete (`EditedAt` stays null) — not in BE-08; not scheduled.
- `attachmentIds` on comments, `attachments` on `Comment`/`IncidentDetail` → 08_03.
- Attachment entity, storage, upload, download → 08_02.
- `@mention` parsing and `Mentioned` notifications → BE-14.
- Broadcasting `CommentAdded` over SignalR (responders-only for internal) → BE-09 (already deferred: post-commit dispatch).
- Emailing/notifying about new comments from the outbox rows → BE-10 / BE-13 / BE-14.

## 8. Decisions

Decided with the user on 2026-10-13:

1. **History row for a comment:** every comment writes a row. `Field` is `Comment` for a public and `InternalComment` for an internal comment, `NewValue` = the comment id. `GET /history` hides `InternalComment` rows (and their share of `totalCount`) from non-responders.
2. **History values** for `AssigneeId`, `AssignedTeamId`, `ApplicationId`, `DuplicateOfId` are resolved to names / incident numbers server-side, one batched lookup per kind per page; a value whose row is gone stays as stored.
3. **Closed incidents** accept comments.
4. **`commentCount`** is per caller: non-responders count public comments only.
5. **A comment does not touch** `Incident.UpdatedAt` / `RowVersion`; the incident row is not written.
6. `CommentDto` stub replaced by the contract shape; cursor/limit/includeTotal added to the contract for `/comments` and `/history`.
7. The request body cap is **128 KiB** (not 64 KiB): 10,000 characters of markup can reach tens of KB once JSON-escaped. A missing `isInternal` binds to `false`.
8. Open for BE-14: the contract enum has `Commented`, DATABASE.md lists `CommentAdded` as a notification type.

Implementation notes: ordering, keyset filter and cursor all run in SQL after the join (SQL Server orders `uniqueidentifier` differently from .NET `Guid.CompareTo`, so a client-side re-sort would break paging across equal timestamps). The atomicity test induces a real foreign-key failure (unknown author) because a comment does not write the incident row, so the concurrent-writer trick of 07_02 does not apply.
