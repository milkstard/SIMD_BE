# Completed items

Items moved here from [`deferred-items.md`](deferred-items.md) once an implementation delivers them.

| Item | Source spec | Completed by | Commit | Notes |
|---|---|---|---|---|
| `SlaClock` + default SLA policy (config-backed `ISlaPolicyProvider`, `Sla:Defaults`) | 04_01 | 05_01 | `057a417` | Setting the due times at creation stays with 05_02 |
| `OutboxMessage` entity/table; domain events → outbox rows in the same `SaveChangesAsync` | 04_01, 04_02 | 05_01 | `057a417` | Migration `AddOutboxMessages`; events cleared only after a successful save |
| Set `AckDueAt`/`ResolveDueAt` at creation using `SlaClock` | 04_01 | 05_02 | `7656473` | Report handler calls `SlaClock.DueTimes` with the configured policy |
| Team-membership resource check on incident endpoints (report half) | 04_01 | 05_02 | `7656473` | `POST /incidents` requires membership of the application's team; the BE-07 half stays deferred |
| Full-text "similar incidents" query using the full-text index (incl. 50k-row seed for the p95 test) | 04_02 | 06_01 | _pending commit_ | `SqlSimilarIncidentSearch` (`FREETEXTTABLE` on Title); opt-in `Category=Performance` test, p95 111.5 ms |
| List/detail queries using `IX_Incidents_Status_Severity` / `IX_Incidents_Application_CreatedAt` | 04_02 | 07_01 | _pending commit_ | Plan test (50k rows) asserts the index seeks for selective filters; `status`/`severity` lists inlined with `EF.Constant` |
| Team-membership resource check on incident endpoints (read half: list, detail) | 04_01 | 07_01 | _pending commit_ | List scoped to the caller's teams; detail 404 for non-members; the write half stays deferred to 07_02/07_03 |
| Team-membership resource check on incident endpoints (write half: transitions, assignment) | 04_01 | 07_02 | _pending commit_ | `IncidentWriteGuard`: non-member and unknown id are both 404; the PATCH half stays deferred to 07_03 |
| Team-membership resource check on incident endpoints (write half: PATCH) | 04_01 | 07_03 | _pending commit_ | `IncidentWriteGuard` reused: non-member and unknown id are both 404; a move to another application needs membership of the target team |
| `Comment` entity, `Comments` table and `CommentAdded` event | 04_01, 04_02 | 08_01 | _pending commit_ | Migration `AddComments`; `Incident.AddComment` writes a `Comment`/`InternalComment` history row; the event reaches the outbox generically |
| Real `commentCount` in `IncidentSummary`/`IncidentDetail` | 05_02 | 08_01 | _pending commit_ | Per-caller count: non-responders count public comments only; `attachments` stays `[]` until 08_03 |
| `Attachment` entity and `Attachments` table | 04_01, 04_02 | 08_02 | _pending commit_ | Migration `AddAttachments`; `IncidentId` nullable until linked (08_03), `CommentId` added; `AttachmentAdded` stays with 08_03 |
| `AttachmentAdded` event | 04_01, 04_02 | 08_03 | _pending commit_ | Raised by `Incident.AttachFiles` (one per file); reaches the outbox generically, with a history row (`Attachment` / `InternalAttachment`) |
| Orphan sweeper for unlinked uploads after a TTL | 08_02 | 08_03 | _pending commit_ | `OrphanAttachmentSweeper` + Worker `OrphanAttachmentJob` (hourly, Redis lock): uploads unlinked after 24 h lose row and blob |
| Link `attachmentIds` on report (+ max 10 rule) | 05_02 | 08_03 | _pending commit_ | `AttachmentLinker` (uploader only, one generic 422); linking is a concurrency-checked update in the same save as the incident |
| Real `attachments` in `IncidentDetail` | 05_02 | 08_03 | _pending commit_ | Incident-level files in the detail, comment files on each comment (batched query) |
| Post-commit dispatch of domain events to SignalR | 04_01 | 09_02 | _pending commit_ | `AppDbContext` hands the events of a committed save to `IPostCommitDispatcher` through `PostCommitHook` (2 s cap, failures logged); the Api sends them through the typed hub context, the Worker registers a no-op |
| Dispatch `IncidentReported` outbox rows (email to the owning team) | 05_01 | 10_03 | _pending commit_ | `IncidentReportedEmailHandler`: `Teams.Email` of the application's owning team; missing team/address is a warning and a skip |
| Email delivery for `IncidentAssigned` and `IncidentTransitioned` (Resolved) outbox rows | 07_02, 07_03 | 10_03 | _pending commit_ | `IncidentAssignedEmailHandler` (new assignee only), `IncidentResolvedEmailHandler` (reporter); the actor is never mailed about their own action |
| SignalR `CommentAdded` (responders-only for internal comments) and attachment events after commit | 08_01, 08_03 | 09_02 | _pending commit_ | Internal comments go to `responders:{teamId}` only; linked files show in the detail read and `CommentAdded.comment.attachments` (no separate event) |
| `AppStatusCalculator` | 04_01 | 11_01 | _pending commit_ | Pure Domain calculator; `Since` derives from the earliest causing incident, no schema change |
| `AppStatusChanged` broadcast | 09_02 | 11_01 | _pending commit_ | Planned by `IncidentBroadcaster` after a post-commit refresh of the cached status; sent to `dashboard:{teamId}` only when the status changed |
| `DashboardChanged` payload: production `IDashboardSummaryReader` | 09_02 | 11_02 | _pending commit_ | `DashboardSummaryReader` recomputes the team's `7d` summary after each commit and overwrites its 5 s cache entry; the Worker keeps the null reader |
| `SlaPolicies` table | 04_02 | 12_01 | _pending commit_ | Migration `AddSlaPolicies` seeds the four defaults; Domain entity `SlaPolicySetting` with `RowVersion` |
| Replace config-backed `ISlaPolicyProvider` (`Sla:Defaults`) with the `SlaPolicies` table | 05_01 | 12_01 | _pending commit_ | `DbSlaPolicyProvider` reads the table; a missing row falls back to `Sla:Defaults` with a warning |
