# Deferred items

Work that a spec in `docs/specs/` explicitly schedules for a later phase or BE item.
Review this file together with the relevant spec before planning an implementation.

Rules:
- Add an item only when a spec explicitly defers it (out of scope / later phase / later BE-NN).
- Never defer something that is mandatory for the spec being implemented.
- When an implementation delivers an item, remove it here and move it to [`completed-items.md`](completed-items.md).

| Item | Source spec | Deferred to | Notes |
|---|---|---|---|
| `Notifications` table | 04_02 | BE-14 | |
| `IncidentDailyStats` table | 04_02 | BE-15 | |
| Notification delivery for `IncidentTransitioned` / `IncidentAssigned` / `IncidentUpdated` / `CommentAdded` / `AttachmentAdded` outbox rows (rows are written by 07_02/07_03/08_01/08_03) | 07_02, 07_03, 08_01, 08_03 | BE-14 | Only the rows are produced in BE-07/BE-08. Spec 10_03 delivered the BE-10 email for `IncidentAssigned` and Resolved transitions; spec 13_02 (BE-13) adds Teams/Slack for those same two triggers only; the rest stays here |
| Rate limiting on `POST /incidents`, `GET /incidents/similar`, `POST /attachments` and `POST /incidents/{id}/comments` | 05_02, 06_01, 08_01, 08_02 | Unassigned (cross-cutting security) | No BE item owns it yet; `similar` is called while the user types |
| `@mention` parsing in comment bodies and `Mentioned` notifications | 08_01 | BE-14 | |
| `NotificationReceived` broadcast to `user:{userId}` | 09_01, 09_02 | BE-14 | Group and method exist after 09_01 |
| Broadcasts for incidents created by the webhook (alert-created incidents) | 09_02 | BE-17 | The SLA-breach half is delivered by 12_04 (Redis pub/sub relay on `incidenthub:changes`); a webhook-created incident must publish on it, or broadcast itself |
| In-app `SlaWarning` / `SlaBreached` notifications (`Notifications` rows) for the `SlaWarning` / `SlaBreached` outbox messages | 12_03 | BE-14 | 12_03 delivers email only |
| Email for `IncidentUpdated`, other transitions, `CommentAdded`, `AttachmentAdded` | 10_03, 13_02 | BE-14 | 10_03 marks these outbox types processed without sending; 13_02 (BE-13) keeps chat to the five existing triggers, so these are not sent on any external channel |
| Endpoint or admin screen to change `User.NotificationPrefs` | 13_02 | Unassigned (no BE item) | BE-13 only honours the stored value (default `Email`); today it changes only in the database. No endpoint in `API-CONTRACT.md` |
| Dashboard `trend`, MTTA and MTTR read from `IncidentDailyStats` instead of raw `Incidents` | 11_02 | BE-15 | 11_02 computes them from `Incidents` because the rollup table does not exist yet |
| Make the sign-in sync (`UserDirectory.EnsureUserAsync`) survive a Redis outage so authenticated endpoints answer from SQL | 11_01 | Unassigned (cross-cutting resilience) | Today every authenticated request fails with 500 when Redis is down; `RedisAppStatusReader` itself falls back to SQL |
