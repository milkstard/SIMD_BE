# Deferred items

Work that a spec in `docs/specs/` explicitly schedules for a later phase or BE item.
Review this file together with the relevant spec before planning an implementation.

Rules:
- Add an item only when a spec explicitly defers it (out of scope / later phase / later BE-NN).
- Never defer something that is mandatory for the spec being implemented.
- When an implementation delivers an item, remove it here and move it to [`completed-items.md`](completed-items.md).

| Item | Source spec | Deferred to | Notes |
|---|---|---|---|
| `AppStatusCalculator` | 04_01 | BE-11 | |
| `SlaPolicies` table | 04_02 | BE-12 | |
| `Notifications` table | 04_02 | BE-14 | |
| `IncidentDailyStats` table | 04_02 | BE-15 | |
| Replace config-backed `ISlaPolicyProvider` (`Sla:Defaults`) with the `SlaPolicies` table | 05_01 | BE-12 | Callers already use the async provider |
| Notification delivery for `IncidentTransitioned` / `IncidentAssigned` / `IncidentUpdated` / `CommentAdded` / `AttachmentAdded` outbox rows (rows are written by 07_02/07_03/08_01/08_03) | 07_02, 07_03, 08_01, 08_03 | BE-13, BE-14 | Only the rows are produced in BE-07/BE-08. Spec 10_03 delivered the BE-10 email for `IncidentAssigned` and Resolved transitions; the rest stays here |
| Rate limiting on `POST /incidents`, `GET /incidents/similar`, `POST /attachments` and `POST /incidents/{id}/comments` | 05_02, 06_01, 08_01, 08_02 | Unassigned (cross-cutting security) | No BE item owns it yet; `similar` is called while the user types |
| `@mention` parsing in comment bodies and `Mentioned` notifications | 08_01 | BE-14 | |
| `DashboardChanged` payload: production `IDashboardSummaryReader` (BE-09 builds the 1/s throttle and the port only) | 09_02 | BE-11 | |
| `AppStatusChanged` broadcast (needs `AppStatusCalculator`) | 09_02 | BE-11 | Method is declared on `IIncidentClient` in 09_01 |
| `NotificationReceived` broadcast to `user:{userId}` | 09_01, 09_02 | BE-14 | Group and method exist after 09_01 |
| Broadcasts for changes made by the Worker/webhook (SLA breach, alert-created incidents) | 09_02 | BE-12, BE-17 | Needs a cross-process path (e.g. Redis pub/sub); BE-09 broadcasts only API-side commits |
| Honour `User.NotificationPrefs` (a `Teams` preference suppresses the email) in the email handlers | 10_03 | BE-13 | 10_03 emails regardless of the stored preference |
| Email for `IncidentUpdated`, other transitions, `CommentAdded`, `AttachmentAdded` | 10_03 | BE-13, BE-14 | 10_03 marks these outbox types processed without sending |
