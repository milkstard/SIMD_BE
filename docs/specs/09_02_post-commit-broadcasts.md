# Spec 09_02: Post-commit broadcasts & dashboard throttle

> Status: **In progress** · Source: BE-09 in docs/BACKEND-SPECS.md · Part 02 of 02

## 1. Purpose & scope

Push committed changes to connected clients. Domain events raised by a save are handed to a post-commit dispatcher (never mid-transaction),
which builds the DTO and sends it through the typed hub context. `DashboardChanged` is throttled to one per second per group.

In scope:
- Post-commit hook in `AppDbContext` and the `IPostCommitDispatcher` port (pulled in from deferred: "Post-commit dispatch of domain events to SignalR").
- Broadcasts: `IncidentCreated`, `IncidentUpdated` (for `IncidentReported`→created; `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated`→updated), `CommentAdded` (internal comments to responders only).
- `DashboardChanged` throttler (1/s/group) behind an `IDashboardSummaryReader` port, tested with a fake (decided 2026-10-13).
- Redis-backplane delivery test across two hosts.

## 2. Dependencies

- **09_01** (groups, `IIncidentClient`, payload records), 05_01 (`AggregateRoot.DomainEvents`, outbox staging in `SaveChangesAsync`), 07_01 (`IncidentProjections.SummaryRows`), 08_01 (`CommentAdded` domain event, comment projection) — the comment broadcast is delivered when 08_01 is built; otherwise it lands with it (see OQ 3).

## 3. Design

### Application
- `Abstractions/IAppTeamReader`: add `GetOwningTeamIdAsync(Guid applicationId, CancellationToken)` (moved here from 09_01).
- `Abstractions/IPostCommitDispatcher` — `Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)`. Implemented in Api (it needs the hub context); the Worker registers a no-op.
- `Abstractions/IDashboardSummaryReader` — `Task<DashboardSummaryDto> GetAsync(Guid teamId, CancellationToken ct)`. No production implementation in BE-09 (BE-11 supplies it); a registered null-object leaves `DashboardChanged` unsent until then.
- `Realtime/IncidentBroadcaster` (Application, pure orchestration, unit-testable with fakes): maps an event to (DTO, target groups) — see §4 table — using a read port `IIncidentBroadcastReader` that returns, for an incident id, its `IncidentSummaryDto` and `OwningTeamId` (same projection as the list; one query).

### Infrastructure
`AppDbContext`: replace the `OnSaved` comment with the real hook. The context collects the events of the staged aggregates **before** clearing them, and after `base.SaveChangesAsync` succeeds hands them to `IPostCommitDispatcher`. Rules:
- Runs only after a successful commit; a failed save dispatches nothing (events stay on the aggregate, as today).
- Dispatch happens in a **fresh DI scope** (own `DbContext`) so reading the DTO never touches the request's change tracker.
- A dispatch failure is caught and logged (`"Broadcast of {EventType} for incident {IncidentId} failed"`), never rethrown: the write already committed and delivery is best-effort (contract §7).
- Not awaited past a short timeout (e.g. 2 s) so a slow Redis cannot hold the HTTP response.
- Span on `InfrastructureTelemetry.Source` around the dispatch.

### Api
`Hubs/HubPostCommitDispatcher : IPostCommitDispatcher` — uses `IHubContext<IncidentsHub, IIncidentClient>`; registered only in the API host.
`Hubs/DashboardThrottle` (singleton): per `dashboard:{teamId}` group, leading edge + trailing coalescing with `TimeProvider` — the first change sends at once, further changes inside the 1-second window set a flag, and one trailing send fires when the window ends. Uses `TimeProvider.CreateTimer` so tests control time. Per-process; with several API instances the cap is per instance (OQ 2).

### Worker
Registers the no-op dispatcher. Nothing broadcasts from the Worker in this part.

## 4. Endpoints & DTOs

No REST change. Event → recipients (all after commit):

| Domain event | Client method | Payload | Groups |
|---|---|---|---|
| `IncidentReported` | `IncidentCreated` | `IncidentSummaryDto` | `dashboard:{teamId}`, `team:{teamId}` |
| `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated` | `IncidentUpdated` | `IncidentSummaryDto` (fresh, with new `rowVersion`) | `dashboard:{teamId}`, `incident:{id}`, `team:{teamId}` |
| `CommentAdded` (public) | `CommentAdded` | `CommentAddedEvent` | `incident:{id}` |
| `CommentAdded` (internal) | `CommentAdded` | `CommentAddedEvent` | `responders:{teamId}` only — never `incident:{id}` |
| any of the above | `DashboardChanged` | via throttle + reader | `dashboard:{teamId}` |

`teamId` = the owning team of the incident's application. A combined edit raising two `IncidentUpdated` events sends one `IncidentUpdated` (coalesced per incident per save, since the DTO is read after the commit). Update API-CONTRACT §7 recipient notes; no `swagger.json` change.

## 5. Acceptance criteria

- [ ] A test client in `incident:{id}` receives `IncidentUpdated` within 1 s of a committed PATCH / transition / assignment, and receives nothing while the transaction is open or after it rolled back.
- [ ] `IncidentCreated` reaches `dashboard:{teamId}` and `team:{teamId}` members of the owning team and nobody else (a second team's client gets nothing).
- [ ] An internal comment is delivered only to `responders:{teamId}`; a Reporter-role client never receives it (hub-level test, not just REST).
- [ ] Two updates inside 1 s to the same dashboard group produce at most two `DashboardChanged` sends in total (one immediate, one trailing) and never more than one per second; a quiet group sends nothing.
- [ ] A broadcast failure (hub context throws) leaves the committed write and its HTTP response intact and is logged.
- [ ] Events are cleared from the aggregate exactly once and only after a successful save; a failed save dispatches nothing.
- [ ] With two API hosts on one Redis, a change made through host A reaches a client connected to host B.
- [ ] Hub sends use the typed client only; times via `TimeProvider`; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Realtime/`: `Map_IncidentReported_TargetsDashboardAndTeam`, `Map_Transitioned_TargetsIncidentGroupToo`, `Map_InternalComment_TargetsRespondersOnly`, `Map_UnknownIncident_SendsNothing`.
`tests/IncidentHub.Api.UnitTests/Hubs/DashboardThrottleTests` (fake `TimeProvider`): `Notify_First_SendsImmediately`, `Notify_BurstWithinWindow_SendsOneTrailing`, `Notify_AfterWindow_SendsAgain`, `Notify_DifferentGroups_AreIndependent`.
`tests/IncidentHub.Api.IntegrationTests/Hubs/BroadcastTests` — `[Trait("Category","Integration")]`, `ApiFactory`:
- `Patch_Committed_WatcherReceivesIncidentUpdated`, `Transition_Committed_WatcherReceivesIncidentUpdated`, `Assign_Committed_WatcherReceivesIncidentUpdated`
- `Report_Committed_TeamClientReceivesIncidentCreated`, `Report_OtherTeamClient_ReceivesNothing`
- `Save_Fails_NothingBroadcast` (stale `If-Match` and duplicate-fingerprint 409), `Broadcast_HubThrows_RequestStillSucceeds`
- `InternalComment_ReporterClient_ReceivesNothing`, `PublicComment_WatcherReceivesCommentAdded` (with 08_01)
- `TwoHosts_SharedRedis_ClientOnOtherHostReceivesEvent`
`tests/IncidentHub.Api.IntegrationTests/Persistence`: `SaveChanges_Success_DispatchesThenClearsEvents`, `SaveChanges_Failure_KeepsEventsAndDispatchesNothing`.

## 7. Out of scope

- Real `DashboardSummary` computation and `DashboardChanged` payload → BE-11 (reader port stays unimplemented until then).
- `AppStatusChanged` (needs `AppStatusCalculator`) → BE-11; `NotificationReceived` → BE-14.
- Broadcasts originating in the Worker (SLA breach, webhook-created incidents) → BE-12 / BE-17 (need a cross-process path, e.g. Redis pub/sub).
- Replaying missed events (clients refetch on reconnect).
- A cluster-wide throttle (per-process in v1).

## 8. Open questions

1. **Doc conflict — `CommentAdded` payload.** The stub sent `CommentDto` (which carries `IncidentNumber`, not `id`); the contract and system design say `{ incidentId, comment }` with the contract `Comment` shape (author, attachments). 09_01 uses `CommentAddedEvent`; the comment DTO is owned by 08_01 — confirm they line up when 08_01 is built.
2. **Throttle scope.** BE-09 says "1/second/group"; with several API instances each throttles locally, so a group can get up to N per second. Acceptable for v1, or use a Redis `SET NX PX 1000` gate (extra Redis call per change)?
3. **Build order.** The comment broadcast depends on 08_01. Implement 09_02 after 08_01 (recommended) or ship it without comments and add that row with 08_01?
4. **Broadcast timeout.** 2 s cap for the dispatch inside `SaveChangesAsync` is a proposal; alternatively dispatch on a background `Channel<>` consumer so no request ever waits (more moving parts, loses events on shutdown).
5. **Incidents created by the Worker/webhook** (BE-17) will not broadcast until a cross-process path exists (see Out of scope). Confirm that is acceptable for v1.

## 9. Decisions (2026-10-13)

1. OQ 1: `CommentAddedEvent(IncidentId, CommentDto)` already matches the contract; no change.
2. OQ 2: the throttle is per process.
3. OQ 3: 08_01 is built, so the comment broadcast ships here.
4. OQ 4: inline dispatch, capped at 2 s; failures and timeouts are logged, never rethrown.
5. OQ 5: Worker/webhook-created changes do not broadcast in v1 (stays deferred to BE-12/BE-17).
6. Broadcast summaries carry the public `commentCount` (internal comments excluded) because they reach Reporters.
7. `AttachmentAdded` broadcasts nothing: files arrive with `IncidentCreated` (report) or `CommentAdded.comment.attachments`.
8. `IDashboardSummaryReader.GetAsync` returns `DashboardSummaryDto?`; the null-object returns `null`, which means "send nothing".
