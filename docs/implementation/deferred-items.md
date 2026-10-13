# Deferred items

Work that a spec in `docs/specs/` explicitly schedules for a later phase or BE item.
Review this file together with the relevant spec before planning an implementation.

Rules:
- Add an item only when a spec explicitly defers it (out of scope / later phase / later BE-NN).
- Never defer something that is mandatory for the spec being implemented.
- When an implementation delivers an item, remove it here and move it to [`completed-items.md`](completed-items.md).

| Item | Source spec | Deferred to | Notes |
|---|---|---|---|
| Post-commit dispatch of domain events to SignalR | 04_01 | BE-09 | |
| `Comment`/`Attachment` entities, tables and events (`CommentAdded`, `AttachmentAdded`) | 04_01, 04_02 | BE-08 | |
| `AppStatusCalculator` | 04_01 | BE-11 | |
| Team-membership resource check on incident endpoints (all but report) | 04_01 | BE-07 | `IAppTeamReader` already exists; report half delivered by 05_02 |
| List/detail queries using `IX_Incidents_Status_Severity` / `IX_Incidents_Application_CreatedAt` | 04_02 | BE-07 | |
| `SlaPolicies` table | 04_02 | BE-12 | |
| `Notifications` table | 04_02 | BE-14 | |
| `IncidentDailyStats` table | 04_02 | BE-15 | |
| Dispatch `IncidentReported` outbox rows (email to the owning team) | 05_01 | BE-10 | Table and same-transaction write are built in 05_01; BE-10 keeps only the dispatcher |
| Replace config-backed `ISlaPolicyProvider` (`Sla:Defaults`) with the `SlaPolicies` table | 05_01 | BE-12 | Callers already use the async provider |
| Link `attachmentIds` on report (+ max 10 rule); non-empty list is `422` until then | 05_02 | BE-08 | |
| Real `commentCount` and `attachments` in `IncidentDetail` (0 / `[]` until then) | 05_02 | BE-08 | |
| Rate limiting on `POST /incidents` and `GET /incidents/similar` | 05_02, 06_01 | Unassigned (cross-cutting security) | No BE item owns it yet; `similar` is called while the user types |
