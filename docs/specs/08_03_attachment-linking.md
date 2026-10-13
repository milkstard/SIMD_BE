# Spec 08_03: Attachment linking (report, comments, detail)

> Status: **Draft** · Source: BE-08 in docs/BACKEND-SPECS.md · Part 03 of 03

## 1. Purpose & scope

Connects the pieces of 08_01 and 08_02: an uploaded attachment is linked to a new incident (`attachmentIds` on
`POST /incidents`) or to a comment (`attachmentIds` on `POST /incidents/{id}/comments`), and appears in `IncidentDetail.attachments`
and `Comment.attachments`.

In scope:
- `attachmentIds` accepted on `ReportIncident` (max 10) and on `AddComment` (max 10).
- Ownership/state checks when linking (uploader only, not already linked).
- `Incident.LinkAttachment(...)`/comment link behaviour, `AttachmentAdded` domain event + outbox row, history row.
- Real `attachments` in `IncidentDetail` and `Comment`.
- Orphan cleanup job for uploads never linked.

Pulled in from `deferred-items.md`:
- "Link `attachmentIds` on report (+ max 10 rule); non-empty list is `422` until then" (05_02).
- "Real `commentCount` and `attachments` in `IncidentDetail`" — the `attachments` half (the count was 08_01).
- "`Comment`/`Attachment` entities, tables and events (`CommentAdded`, `AttachmentAdded`)" — `AttachmentAdded`.

## 2. Dependencies

- **08_01** (`Comment`, `CommentDto`, `AddComment`), **08_02** (`Attachment`, `IFileStore`, `AttachmentDto`).
- 05_02 (`ReportIncident`), 07_01 (detail projection), 05_01 (outbox).

## 3. Design

### Domain
- `Attachment.LinkTo(Guid incidentId, Guid? commentId)` — only from the unlinked state; linking twice → `InvalidOperationException`.
- `Incident.AttachFiles(IReadOnlyCollection<Attachment> attachments, Actor actor, DateTimeOffset at, Guid? commentId = null)`: links each, appends one history row per attachment (`Field = "Attachment"`, `NewValue` = file name; internal-comment rule per 08_01 OQ 1), raises `AttachmentAdded(IncidentId, AttachmentId, CommentId?, ActorId, At)`. `Attachment.MaxPerAction = 10`.
- On `Incident.Report`, attachments are linked in the same call that creates the history row, so there is no window with a linked-but-unreported file.

### Application
- `ReportIncidentValidator`: replace the "must be empty" rule with max 10, no empty/duplicate ids. `AddCommentValidator`: same.
- `IAttachmentLinker` (internal Application service, not a port): `LoadLinkableAsync(IReadOnlyList<Guid> ids, Guid callerId, ct)` returns the tracked attachments or throws `ValidationException` on `attachmentIds` when any id is unknown, uploaded by someone else, or already linked. One generic message for all three (no existence leak). `Infected` files cannot be linked; `Pending` files can (the download stays blocked until scanned).
- `ReportIncidentHandler`: calls the linker before `Incident.Report`, passes the attachments, single `SaveChangesAsync` as today.
- `AddCommentHandler`: same, with the new comment's id.
- `IncidentDetailMapper` / projection: `attachments` loaded with the detail (second query `AsNoTracking()` + `Select` into `AttachmentDto`, ordered `CreatedAt`); incident-level list = attachments linked to the incident with no comment; comment attachments shown on the comment. Attachments linked to an **internal** comment are filtered out for non-responders (consistent with 08_01).
- `CommentDto.Attachments` filled by `ListComments` with one batched query per page (no N+1).

### Infrastructure
- Migration (only if 08_02 did not already include the column): FK `Attachments.CommentId → Comments.Id`, index `(CommentId)`.
- `Worker/OrphanAttachmentJob` — hourly: unlinked attachments older than 24 h (`IncidentId IS NULL`, backed by the filtered index from 08_02) have their blob deleted (`IFileStore.DeleteAsync`) and the row removed. This is the only hard delete of attachments (DATABASE.md rule update, OQ 2). Single instance via the Redis lock.

### Api
- `ReportIncidentRequest.AttachmentIds` and `AddCommentRequest.AttachmentIds` bound (contract fields already exist); no new routes.
- Idempotency body hash includes `attachmentIds`.

### Worker
`OrphanAttachmentJob` as above.

## 4. Endpoints & DTOs

No new endpoints. Behaviour changes:

| Endpoint | Change |
|---|---|
| `POST /incidents` | `attachmentIds` (max 10) accepted; invalid ids → `422` on `attachmentIds`; the `201` `IncidentDetail` contains the attachments |
| `POST /incidents/{id}/comments` | `attachmentIds` (max 10) accepted; `Comment.attachments` filled |
| `GET /incidents/{number}` | `attachments` filled |
| `GET /incidents/{id}/comments` | `attachments` filled per comment |

Update `API-CONTRACT.md`: remove the "`attachmentIds` must be empty until BE-08 (`422`)" note, document max 10 / uploader-only / single-use, the 24 h orphan rule, and the internal-comment attachment visibility. Regenerate `swagger.json`.

## 5. Acceptance criteria

- [ ] Reporting with up to 10 of the caller's own unlinked attachments links them, writes the history and `AttachmentAdded` outbox rows in the same `SaveChangesAsync` as the incident, and returns them in `attachments`.
- [ ] More than 10 ids, duplicate ids, an unknown id, an id uploaded by another user, an already linked id, or an `Infected` file → `422` on `attachmentIds`; nothing is written.
- [ ] A comment with `attachmentIds` links them to that comment and incident; `Comment.attachments` lists them; a Reporter never sees attachments of internal comments (list, detail, download).
- [ ] The same attachment cannot be linked twice, also under two concurrent requests (exactly one wins; the other gets `422`).
- [ ] `IncidentDetail.attachments` and `Comment.attachments` carry the real values; `scanStatus` is truthful (download still blocked until `Clean`).
- [ ] Rollback test: a failing save leaves the attachments unlinked (and the incident/comment absent).
- [ ] Unlinked uploads older than 24 h are deleted (row and blob) by `OrphanAttachmentJob`; linked or younger ones are untouched.
- [ ] List and comment queries stay free of N+1 (query count asserted); build + `dotnet format` clean; `API-CONTRACT.md` + `swagger.json` updated; the 05_02 test that expected `422` for any attachment id is replaced.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Incidents/`
- `AttachmentLinkTests`: `LinkTo_Unlinked_Sets`, `LinkTo_AlreadyLinked_Throws`, `AttachFiles_AppendsHistoryAndRaisesEvent`, `AttachFiles_MoreThanTen_Throws`.

`tests/IncidentHub.Application.UnitTests/`
- `ReportIncidentValidatorTests`: `Validate_ElevenAttachmentIds_HasError`, `Validate_DuplicateAttachmentIds_HasError`, `Validate_TenAttachmentIds_NoErrors`.
- `AddCommentValidatorTests`: same three cases.
- `AttachmentLinkerTests`: `LoadLinkable_OtherUsersFile_Throws`, `LoadLinkable_AlreadyLinked_Throws`, `LoadLinkable_Infected_Throws`, `LoadLinkable_Pending_Succeeds`, `LoadLinkable_UnknownId_SameMessageAsForeign`.

`tests/IncidentHub.Api.IntegrationTests/` — `[Trait("Category","Integration")]`:
- `ReportIncidentAttachmentTests`: `Report_WithAttachments_LinksAndReturnsThem`, `Report_ForeignAttachment_Returns422`, `Report_ElevenAttachments_Returns422`, `Report_WritesAttachmentOutboxInSameSave`, `Report_SameIdempotencyKey_LinksOnce`.
- `CommentAttachmentTests`: `Comment_WithAttachment_ListsOnComment`, `Comment_InternalAttachment_HiddenFromReporter`, `Comment_AttachmentLinkedConcurrently_OneWins`, `Detail_ListsIncidentAttachments`, `Comments_List_UsesConstantQueryCount`.
- `OrphanAttachmentJobTests`: `Sweep_OldUnlinked_DeletesRowAndBlob`, `Sweep_Linked_Untouched`, `Sweep_Recent_Untouched` (uses a fake `TimeProvider`).
- Rollback test as in 07_02 for the combined incident + attachment link.

## 7. Out of scope

- Adding attachments to an existing incident without a comment (no endpoint in API-CONTRACT) — not scheduled.
- Removing/replacing a linked attachment; comment edit.
- Notification delivery for `AttachmentAdded` → BE-10 / BE-13 / BE-14 (rows only).
- SignalR broadcast of attachment events → BE-09.
- Real virus-scanner integration (08_02 out of scope).

## 8. Open questions

1. **`Pending` attachments at link time.** Proposed: allowed (the user can't wait for a scan before submitting a report); `Infected` is rejected. Confirm.
2. **DATABASE.md rule "nothing is hard-deleted".** The orphan sweeper hard-deletes never-linked uploads (draft-stage rows, which the rule explicitly excepts) — confirm this reading, and update the rule text to name uploads.
3. **Orphan TTL** 24 h is a proposal; BACKEND-SPECS does not give one.
4. **Does linking to a comment on a `Closed` incident work?** Follows 08_01 OQ 3 (allowed).
5. **Same-team uploads.** The uploader-only linking rule blocks a teammate from reusing someone's upload; intentional (prevents guessing ids), confirm.
