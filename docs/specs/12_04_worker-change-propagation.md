# Spec 12_04: Worker change propagation (cache refresh and broadcast)

> Status: **Approved** (2026-10-19) · Source: BE-12 in docs/BACKEND-SPECS.md · Part 4 of 4

## 1. Purpose & scope

When the `SlaMonitor` (12_02) changes an incident, API clients still see the old state until the next read: the Worker's post-commit hook is a no-op, so no SignalR event is sent and the app-status / dashboard-summary caches live out their TTL.

This part lets a Worker commit reach the API: it refreshes the caches and broadcasts `IncidentUpdated` (`isSlaBreached`), `AppStatusChanged` and `DashboardChanged`.

Pulled in from `deferred-items.md`:
- "Invalidate the app-status and summary caches (and broadcast) when the Worker changes an incident" (11_01, 11_02, -> BE-12).
- The SLA-breach half of "Broadcasts for changes made by the Worker/webhook" (09_02, -> BE-12, BE-17). The alert-created-incident half stays with BE-17.

## 2. Dependencies

12_02 (the Worker change), 09_02 (`IPostCommitDispatcher`, `IncidentBroadcaster`, `HubPostCommitDispatcher`, throttle), 11_01/11_02 (`IAppStatusReader.RefreshAsync`, `IDashboardSummaryReader`), Redis (already a hard dependency; no new store or broker, so no ADR, OQ 1).

## 3. Design

### Approach (OQ 1)
Redis pub/sub channel `incidenthub:changes`. After the Worker commits, its post-commit dispatcher publishes the committed `IncidentUpdated` events as small JSON messages (`IncidentId` and an origin marker; the receiver re-reads the incident). Each API instance subscribes at startup and feeds the messages into the same `IncidentBroadcaster` path a local commit uses (summary re-read after commit, status refresh, hub send). Delivery is best-effort, like SignalR: a missed message is healed by the cache TTL and the client's refetch on reconnect.

### Application
- `IWorkerChangePublisher` (port): `PublishAsync(IReadOnlyList<IDomainEvent>, ct)`.
- `IWorkerChangeSubscriber`/handler `IncidentChangeRelay`: given an incident id, runs the existing broadcaster plan (same code, no copy).

### Infrastructure
- `RedisWorkerChangePublisher`, `RedisChangeSubscriber` (`IHostedService` in the API host only), message record + serializer; Redis errors logged and swallowed (never fail the Worker save).
- Worker: `IPostCommitDispatcher` replaced by `WorkerPostCommitDispatcher` calling the publisher (the Worker keeps the null `IDashboardSummaryReader`; the API computes the summary).
- Span `changes.publish` / `changes.relay` on `InfrastructureTelemetry.Source`.

### Api
`ChangeRelayHostedService` registered in the API host; hub sends stay inside `HubPostCommitDispatcher`. An instance that also commits locally does not double-send (messages carry an origin marker, the subscriber ignores its own).

### Event scope
Only `IncidentUpdated` from the Worker is relayed (SLA breach). `SlaWarning` changes no API-visible state and is not relayed (OQ 2).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` section 7 changes: remove "Changes made by the Worker (SLA breach ...) are not broadcast yet" for SLA breaches (webhook-created incidents stay listed), and in the `app-status` note drop "can show up to 60 s late" for breaches. `swagger.json` unchanged.

## 5. Acceptance criteria

- [x] After the monitor flags an incident, a connected dashboard client receives `IncidentUpdated` with `isSlaBreached: true` within a few seconds of the Worker commit.
- [x] The dashboard-summary cache is refreshed on that change (no wait for TTL). The app-status cache needs no refresh and no `AppStatusChanged` is sent: a breach does not change the derived status (section 9, item 4).
- [x] Redis pub/sub being down does not fail or roll back the Worker save; the failure is logged.
- [x] With two API instances each connected client gets the event exactly once; an API-origin commit is not relayed (section 9, item 6).
- [x] Internal-only data never travels: the relayed message carries ids, the receiver reads and filters per group as today.

## 6. Test plan

- `Application.UnitTests`: `Relay_UnknownIncident_DoesNothing`, `Relay_KnownIncident_UsesBroadcasterPlan`.
- `Api.UnitTests`: `RedisChangeSubscriber_MalformedMessage_IsIgnoredAndLogged`, `Subscriber_OwnOrigin_IsIgnored`.
- `Api.IntegrationTests/Sla/` `[Trait("Category","Integration")]`: `Breach_WorkerCommit_ClientReceivesIncidentUpdated`, `Breach_WorkerCommit_RefreshesAppStatusAndSummaryCache`, `Breach_RedisPublishFails_SaveStillCommits`, `Breach_StatusUnchanged_NoAppStatusChanged`.

## 7. Out of scope

- Broadcasts for webhook-created incidents -> BE-17 (stays in deferred items, row narrowed).
- A message broker or outbox-based relay (ADR required).
- Replaying missed events.
- Relaying `NotificationReceived` -> BE-14.

## 8. Open questions

1. **Mechanism.** Redis pub/sub (proposed; Redis already holds the SignalR backplane and caches, so no new infrastructure) vs. the API polling `IncidentHistory`/`UpdatedAt` (no new channel, adds latency) vs. an ADR for a broker (forbidden by CLAUDE.md without one). Confirm pub/sub.
2. **Relay `SlaWarning`?** It changes no visible field. Proposed: no.
3. **Is this part mandatory for BE-12?** Both deferred rows name BE-12, and the acceptance criteria of BE-12 do not require it. It is included because the rows assign it; say if it should move out to a later chore.

## 9. Decisions (2026-10-19)

1. **Mechanism (OQ 1):** Redis pub/sub on `incidenthub:changes`, confirmed. No new store, so no ADR.
2. **`SlaWarning` (OQ 2):** not relayed. Only `IncidentUpdated` is.
3. **Scope (OQ 3):** implemented in BE-12 as the deferred rows assign it.
4. **App status:** a breach only sets `IsSlaBreached`, which the derived status ignores, so the app-status cache can never be stale after one. `IncidentBroadcaster` keeps its status fields (`Severity`, `Environment`, `ApplicationId`) and no refresh or `AppStatusChanged` follows a breach; the summary (`slaBreached`) is refreshed through the normal `DashboardChanged` path. Acceptance criterion 2 is read that way.
5. **No new ports:** `IPostCommitDispatcher` is already the port the Worker's save calls and the API's `HubPostCommitDispatcher` is the sink, so `IWorkerChangePublisher`, `WorkerPostCommitDispatcher` and `IncidentChangeRelay` were not added. The Worker's dispatcher is `RedisChangePublisher` (replaces the no-op), the API runs `RedisChangeSubscriber`, both in `Infrastructure/Realtime/`. The test names in section 6 became `RedisChangePublisherTests`, `RedisChangeSubscriberTests` and `Sla/WorkerChangePropagationTests`; `Relay_*` cases are `IncidentBroadcasterTests.IncidentBroadcaster_SlaBreach_*` (the unknown-incident case was already covered).
6. **One relayer per message:** the SignalR Redis backplane already delivers a send from one instance to the clients of every instance, so every instance relaying would give each client N copies. Each message therefore carries a `MessageId` and an instance relays it only after `SET changes:claim:{id} NX` (30 s TTL) succeeds; the others skip it. If the claim itself fails the message is skipped (the TTL and the client's refetch heal it). The `Origin` marker stays as a cheap guard: a process never relays its own publish.
7. **Errors:** publisher failures (`RedisException`, `TimeoutException`) are logged as Warning and never reach the Worker's save (`PostCommitHook` also caps the call at 2 s). The subscriber retries a failed subscription every 5 s; a bad message is logged and skipped; a stalled dispatch is cut at the same 2 s cap.
8. **Known limit, pre-existing:** a connection in both `dashboard:{teamId}` and `team:{teamId}` receives an `IncidentUpdated` once per group (the event targets both). It is documented in API-CONTRACT section 7; the relay does not add to it.
9. **Summary cache scope:** the pushed summary is the owning team's `7d` one. A user of several teams has another cache key, which heals within the 5 s TTL (as for API commits).

10. **Review follow-ups (2026-10-19):** after the Approved review the subscriber loop catches any exception, not only `RedisException`/`TimeoutException`, so an unexpected failure retries instead of stopping the API host (SEC-1, QA, code review L2), and it unsubscribes the queue before every resubscribe and on shutdown (L1). New test `Execute_SubscribeFails_LogsAndResubscribesAfterTheDelay`.
