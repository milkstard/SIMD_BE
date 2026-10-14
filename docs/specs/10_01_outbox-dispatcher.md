# Spec 10_01: Outbox dispatcher (claim, retry, back-off)

> Status: **Accepted** · Source: BE-10 in docs/BACKEND-SPECS.md · Part 1 of 3

## 1. Purpose & scope

The reliable delivery engine for the transactional outbox written by 05_01. The Worker polls `OutboxMessages`, claims a batch,
hands each message to the handlers registered for its type, and records the outcome. No sender and no concrete trigger exists in this
part: handlers are an extension point (10_03 adds the email ones, BE-13/BE-14 add more), so the engine is tested with fake handlers.

In scope:
- `OutboxDispatcher` `BackgroundService` in the Worker: poll every 2 s, batch of 50.
- Row claim with `UPDLOCK, READPAST` so two instances never take the same row.
- Failure bookkeeping: `Attempts`, `Error`, exponential back-off with jitter via a new `NextAttemptAt` column, a max-attempts ceiling.
- `IOutboxMessageHandler` port and event-type registry (type name → CLR event record).

Pulled in from `deferred-items.md`: "Dispatch `IncidentReported` outbox rows" (05_01, → BE-10; delivered across 10_01–10_03).

## 2. Dependencies

- **05_01** (`OutboxMessage`, `OutboxMessages` table, `AppDbContext` staging, `DomainEventTypeTests` guarding unique event names).
- 04_01 (`IDomainEvent` records), 08_02/08_03 (Worker host, Redis, `IDistributedLock`, job pattern to copy).
- Later parts: 10_02 (sender) and 10_03 (handlers) depend on this one.

## 3. Design

### Domain
No change.

### Application (`IncidentHub.Application/Outbox/`)
- `IOutboxMessageHandler` — `string EventType { get; }` and `Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken ct)`.
  A message may have several handlers (email now, in-app and Teams later). Handlers must be idempotent by `outboxMessageId`
  as far as their channel allows (CLAUDE.md). A handler throws to request a retry; returning normally means done or deliberately skipped.
- `NonRetryableOutboxException` — a handler throws it for a failure no retry can fix; the dispatcher then parks the message at once.
- `OutboxDispatcherOptions` (section `Outbox:Dispatcher`): `PollInterval` 2 s, `BatchSize` 50, `MaxAttempts` 8, `BackoffBase` 10 s, `BackoffCap` 1 h,
  `JitterFraction` 0.2. Validator + `ValidateOnStart()` (all positive, jitter 0–1, cap ≥ base).
- `OutboxBackoff.NextAttemptAt(int attempts, DateTimeOffset now, Random jitter)` — pure: `min(cap, base · 2^(attempts-1))` ± jitter. Unit-testable.

### Infrastructure (`IncidentHub.Infrastructure/Outbox/`)
- `OutboxMessage`: add `NextAttemptAt` (`DateTimeOffset?`), and behaviour `MarkProcessed(at)`, `MarkFailed(at, error, nextAttemptAt)`
  (increments `Attempts`, truncates `Error` to 2,000 chars, clears nothing else) and `Park(at, error)` (sets `Attempts = MaxAttempts`-equivalent via
  the dispatcher's ceiling, see below). `ProcessedAt` is set only on success or a deliberate skip; a parked message keeps `ProcessedAt = null`
  so it stays visible to operators.
- `OutboxMessageConfiguration`: `NextAttemptAt datetimeoffset` null. Rebuild `IX_OutboxMessages_Unprocessed` (filtered `ProcessedAt IS NULL`) as
  key `ProcessedAt`, `INCLUDE (OccurredAt, NextAttemptAt, Attempts)` so the poll is index-only.
- `OutboxEventTypes` — registry built once by scanning `IDomainEvent` records in the Domain assembly (names are unique and ≤ 50 chars, guarded by
  `DomainEventTypeTests`); `bool TryDeserialize(OutboxMessage, out IDomainEvent)` using `OutboxJson.Options`.
- `OutboxDispatchRunner` (scoped, so integration tests run one pass like `AttachmentScanRunner`): `Task<int> RunOnceAsync(ct)`:
  1. Begin a transaction on the `AppDbContext` connection.
  2. Claim: `SELECT TOP (@batch) … FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL AND Attempts < @max AND (NextAttemptAt IS NULL OR NextAttemptAt <= @now) ORDER BY OccurredAt`
     via `FromSqlInterpolated`; `@now` comes from `TimeProvider`.
  3. For each message, in `OccurredAt` order: deserialize; unknown type or bad JSON → `Park`; no handler registered for the type → `MarkProcessed`
     (nothing wants it yet; `Error` stays null); otherwise run every handler for the type. All succeed → `MarkProcessed`. Any throws →
     `MarkFailed` (back-off computed from the new attempt count); `NonRetryableOutboxException` → `Park`. One message's failure never stops the batch.
  4. `SaveChangesAsync` + commit once per batch. A crash before commit rolls the whole batch back and releases the locks, so rows are redelivered:
     **delivery is at-least-once** (OQ 1).
  Returns the number of rows claimed.
- Span `outbox.dispatch` per batch and `outbox.handle` per message on `InfrastructureTelemetry.Source`; structured logs
  (`"Outbox message {OutboxMessageId} of type {EventType} failed on attempt {Attempts}"`, never the payload).
- DI: `AddOutbox()` registers options, the registry, the runner; handlers register as `IOutboxMessageHandler` (none in this part).
- Migration `AddOutboxNextAttemptAt` (with `Down`).

### Worker
- `Jobs/OutboxDispatcher : BackgroundService` — loop: `RunOnceAsync` in a fresh scope; when a pass returns a full batch loop again at once, otherwise
  wait `PollInterval`. Exceptions are logged and the loop continues; cancellation stops it. No Redis lock: the claim is the mutual exclusion
  (several replicas may run, unlike `SlaMonitor`).
- Registered in `Program.cs`.

### Api
Nothing. The API never dispatches (acceptance: only the dispatcher sends).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `swagger.json` unchanged. **Update `DATABASE.md`**: `OutboxMessages.NextAttemptAt` (`datetimeoffset`, nullable — earliest
time the dispatcher may retry; null = now) and the new `IX_OutboxMessages_Unprocessed` definition; note that `Attempts` at the configured maximum means the
message is parked (no longer claimed, `ProcessedAt` still null).

## 5. Acceptance criteria

- [ ] The dispatcher polls every 2 s and claims at most 50 rows per pass, oldest `OccurredAt` first.
- [ ] A message whose handlers all succeed gets `ProcessedAt` set and is never claimed again.
- [ ] A handler failure does not lose the message: `Attempts` increments, `Error` holds the (truncated) message, `NextAttemptAt` is set by exponential back-off with jitter, the row is not deleted and is not claimed again before `NextAttemptAt`.
- [ ] A message whose `Attempts` reached `MaxAttempts` is no longer claimed but stays `ProcessedAt = null` with its last `Error`.
- [ ] Two runners executing concurrently never process the same message (claimed-row test: N rows, 2+ runners, each handler call recorded once per id).
- [ ] A message with no registered handler is marked processed; an unknown event type or unreadable payload is parked and logged without stopping the batch.
- [ ] One failing message does not prevent the others in the batch from completing.
- [ ] A crash (exception outside handlers) before commit leaves all claimed rows unprocessed and claimable.
- [ ] No handler, controller or Application type touches `OutboxMessage`; the API host registers no dispatcher; build (warnings as errors) and `dotnet format` clean.
- [ ] Migration `AddOutboxNextAttemptAt` applies on an empty database and on top of the existing ones; `Down` reverts; `DATABASE.md` updated.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Outbox/`
- `OutboxBackoffTests`: `NextAttemptAt_FirstAttempt_UsesBase`, `NextAttemptAt_Doubles_PerAttempt`, `NextAttemptAt_BeyondCap_ClampsToCap`, `NextAttemptAt_Jitter_StaysWithinFraction`.
- `OutboxDispatcherOptionsValidatorTests`: `Validate_Defaults_Succeeds`, `Validate_CapBelowBase_Fails`, `Validate_JitterOutOfRange_Fails`, `Validate_NonPositiveBatch_Fails`.

`tests/IncidentHub.Api.UnitTests/` (where Infrastructure-facing units live)
- `OutboxEventTypesTests`: `TryDeserialize_EachDomainEvent_RoundTrips`, `TryDeserialize_UnknownType_ReturnsFalse`, `TryDeserialize_BadJson_ReturnsFalse`.

`tests/IncidentHub.Api.IntegrationTests/Outbox/OutboxDispatchRunnerTests` — `[Trait("Category","Integration")]`, SQL Server container, fake `TimeProvider`, fake handlers:
- `RunOnce_HandlerSucceeds_MarksProcessed`
- `RunOnce_HandlerThrows_IncrementsAttemptsKeepsRowAndSetsNextAttempt`
- `RunOnce_BeforeNextAttempt_DoesNotClaim`, `RunOnce_AfterNextAttempt_RetriesAndSucceeds`
- `RunOnce_MaxAttemptsReached_ParksRow`
- `RunOnce_NonRetryable_ParksImmediately`
- `RunOnce_NoHandler_MarksProcessed`, `RunOnce_UnknownType_Parks`
- `RunOnce_OneFailure_OthersStillProcessed`
- `RunOnce_MoreThanBatch_ClaimsBatchSizeOldestFirst`
- `RunOnce_TwoRunnersConcurrently_EachMessageHandledOnce` (claimed-row test)
- `RunOnce_CommitFails_RowsStayClaimable`
- `Migrate_AddOutboxNextAttemptAt_UpAndDown`, `Schema_OutboxMessages_MatchesDatabaseDoc`

`OutboxDispatcherJobTests` (Worker, hosted with a fake runner): `Job_FullBatch_LoopsWithoutWaiting`, `Job_RunnerThrows_KeepsRunning`.

## 7. Out of scope

- `INotificationSender`, `EmailSender`, Polly, templates → 10_02.
- The email triggers and recipient lookup → 10_03.
- Teams/Slack and per-user channel preference → BE-13; in-app notifications → BE-14.
- An endpoint or UI to inspect or requeue parked messages (not scheduled).
- Exactly-once delivery (the engine is at-least-once; senders dedupe where the channel allows).

## 8. Open questions

1. **Delivery guarantee:** claim and outcome share one transaction per batch (CLAUDE.md "claims … with `UPDLOCK, READPAST`"), so a crash after a successful send but before commit re-sends. Accepted as at-least-once; email dedupe is limited to a `Message-Id` header derived from the outbox id (10_02). The locks are held for the batch's send time (bounded by the Polly timeouts); say if a lease column (claim, commit, send, record) is preferred.
2. **Schema vs `DATABASE.md`:** `NextAttemptAt` is not in `DATABASE.md`; decided with the user on 2026-10-14 to add it (above). BACKEND-SPECS BE-10 lists the table among its requirements, but 05_01 already built it.
3. **Parked messages:** no max-attempts or dead-letter behaviour is stated in any doc; the `MaxAttempts` ceiling (8, about 20 minutes to 1 hour of retries at the defaults) is a proposal.

## 9. Decisions (2026-10-14)

- OQ 1 and OQ 3 kept as proposed: one transaction per batch (at-least-once), `MaxAttempts` 8.
- `Park` takes the ceiling as a parameter (`Park(error, maxAttempts)`); it has no use for a timestamp.
- The runner is behind `IOutboxDispatchRunner` so the Worker job can be tested with a fake; those tests live in a new `tests/IncidentHub.Worker.UnitTests` project.

## 10. Review follow-ups (2026-10-14)

Decided after `/review-spec` (QA, security, code review all passed; these close the items that matter before 10_02/10_03 add real handlers):
- **Handler scope (code review M2):** the runner takes `IServiceScopeFactory` and resolves the handlers in a scope per message, so a handler gets its own `IAppDbContext`, outside the claim transaction. Contract written on `IOutboxMessageHandler`.
- **Time limits (SEC-1):** new options `HandlerTimeout` (60 s since 10_02, was 30 s, so Polly's 45 s total send timeout fits inside it; cancels the handlers, counted as a failed attempt) and `PassBudget` (60 s; no further message is started after it). Row locks are therefore held for at most `PassBudget` plus one `HandlerTimeout`.
- **Shutdown (code review M1):** when the host stops, the message in flight is left untouched and the outcomes already recorded are saved with `CancellationToken.None`, so a deploy does not resend them.
- **Logging (SEC-3, L2):** a message with no handler logs at Information; every "parked" path logs at Error.
- **Accepted limitation (QA M1):** the index is keyed on `ProcessedAt` (as §3 and `DATABASE.md` say), so the ordered claim locks every eligible row and replicas take turns instead of splitting the backlog. No double processing. Re-key the index on `OccurredAt` if scale-out throughput is ever needed. The "index-only" wording in §3 is wrong: the claim reads whole rows, only the `WHERE` is evaluated from the index.
- **Exception text (SEC-2):** `Error` and the logs keep the exception message (§5 requires it). The handler contract says messages must not carry addresses, webhook URLs or secrets; 10_02 senders must throw safe messages.

## 11. Review round 2 fixes (2026-10-14)

- **Shutdown (L1/SEC-C):** the outcomes recorded in a pass are always saved and committed with `CancellationToken.None` (bounded by the SQL command timeout); once the host is stopping no further message is started. The `stopping` flag is gone.
- **Timeout text (L3):** a handler that outruns `HandlerTimeout` is recorded as `Handler timed out after <timeout>.` and counts as a failed attempt.
- **Option bounds (L4/SEC-D):** `BatchSize` 1 to 1000, `HandlerTimeout` at most 5 min, `PassBudget` at most 10 min, `BackoffCap` at most 1 day (a larger value makes `CancelAfter` or `AddSeconds` throw at runtime).
- **Doc drift (L5):** `OutboxBackoff.NextAttemptAt` takes the options: `NextAttemptAt(int attempts, DateTimeOffset now, OutboxDispatcherOptions options, Random jitter)`.
