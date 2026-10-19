# Spec 12_02: SLA monitor (warning and breach detection)

> Status: **Draft** · Source: BE-12 in docs/BACKEND-SPECS.md · Part 2 of 4

## 1. Purpose & scope

A Worker `SlaMonitor` that runs every minute under a Redis lock, and for open incidents:
- raises one `SlaWarning` outbox message when the incident reaches `EscalateAtPercent` of its resolve window,
- on breach sets `IsSlaBreached = true` and raises one `SlaBreached` outbox message.

This part produces the state and the outbox rows only; who is mailed is 12_03.

## 2. Dependencies

12_01 (policies in the table), BE-04/05 (`Incident.MarkSlaBreached`, `SlaClock`), BE-10 (outbox runner), `IDistributedLock` / `RedisDistributedLock` (08_02), `OrphanAttachmentJob` as the job template.

## 3. Design

### Domain
- `SlaClock.DueTimes` also returns `WarnAt` = start + `ResolveMinutes * EscalateAtPercent / 100` (`SlaDueTimes(AckDueAt, ResolveDueAt, WarnAt)`).
- `Incident`: new `SlaWarnAt` (set with the due times on create and in `ChangeSeverity`) and `SlaWarnedAt` (null until the warning is raised; cleared by `ChangeSeverity`, because the clock restarts). `SetSlaDueTimes`/`ChangeSeverity` take the warn time.
- `Incident.MarkSlaWarned(at)`: idempotent; sets `SlaWarnedAt`, raises `IncidentSlaWarning`; no history row (OQ 3).
- `Incident.MarkSlaBreached(at)` (exists): additionally raises `IncidentSlaBreached` (new event) so the outbox gets a `SlaBreached` row. Idempotent: already breached does nothing.
- New events in `Incidents/Events/`: `SlaWarning(IncidentId, At)`, `SlaBreached(IncidentId, At)` (named to match the contract's outbox types, OQ 4); picked up by `OutboxEventTypes` automatically (type name = outbox `Type`). Names vs. the contract's outbox types `SlaWarning`/`SlaBreached`: OQ 4.
- Eligibility (pure helper `SlaEvaluation`): open incident (not `Resolved`/`Closed`); `OnHold` still counts (wall-clock SLA, OQ 5).

### Application
`Sla/EvaluateSla/` `EvaluateSlaCommand` + handler (the unit of work the job calls, testable without the host): loads one batch of candidates, calls `MarkSlaWarned`/`MarkSlaBreached` with `TimeProvider.GetUtcNow()`, `SaveChangesAsync` (events become outbox rows in the same save). Candidates:
- warning: `SlaWarnedAt IS NULL AND SlaWarnAt <= now AND IsSlaBreached = 0` and open;
- breach: `ResolveDueAt <= now AND IsSlaBreached = 0` and open.
Batch of 100, ordered by due time, repeated until a pass returns fewer than the batch; `AsTracking` here is justified (aggregates are mutated). One incident failing to save is logged and skipped; `DbUpdateConcurrencyException` (a user edited at the same moment) is retried on the next tick. An incident that crosses warn and breach in the same tick gets the breach only, no stale warning (OQ 6).

### Infrastructure
- Migration `AddIncidentSlaWarn`: `SlaWarnAt datetimeoffset NULL`, `SlaWarnedAt datetimeoffset NULL`; backfill `SlaWarnAt` for open incidents from the current policy (OQ 1); index `IX_Incidents_SlaCandidates` on `(ResolveDueAt)` INCLUDE `(SlaWarnAt, SlaWarnedAt)` filtered `WHERE IsSlaBreached = 0 AND Status NOT IN ('Resolved','Closed')`. Check the plan before adding (CLAUDE.md data-access rule).
- `IncidentConfiguration` maps the new columns; `DATABASE.md` updated.

### Worker
`Jobs/SlaMonitor : BackgroundService`, same shape as `OrphanAttachmentJob`: `PeriodicTimer(1 min)`, lock name `sla-monitor` with TTL 2 min, `await using` the handle, scope per run, `ISender.Send(EvaluateSlaCommand)`, error logged and loop continues. Registered in `Worker/Program.cs`. Span `sla.evaluate` on `InfrastructureTelemetry.Source`.

## 4. Endpoints & DTOs

None new. `IncidentSummary`/`IncidentDetail` already expose `isSlaBreached`, `ackDueAt`, `resolveDueAt`; `SlaWarnAt`/`SlaWarnedAt` stay internal. `contracts/swagger.json` unchanged (verify). `API-CONTRACT.md`: no change expected (the existing `SLA breached` history field is already listed).

## 5. Acceptance criteria

- [ ] With two worker replicas, only one runs the evaluation per tick (lock contention test).
- [ ] An incident crossing `EscalateAtPercent` produces exactly one `SlaWarning` outbox row, however many ticks follow.
- [ ] An open incident past `ResolveDueAt` gets `IsSlaBreached = true`, one history row (system actor) and exactly one `SlaBreached` outbox row; later ticks add nothing.
- [ ] Resolved, Closed incidents are never warned or breached.
- [ ] Changing severity restarts the clock: due times, `SlaWarnAt` are recomputed and `SlaWarnedAt` is cleared, so a new warning can follow.
- [ ] Policy changes do not move `SlaWarnAt` of existing incidents.
- [ ] A failure on one incident does not stop the batch or crash the host.
- [ ] All status/outbox writes use `TimeProvider`; no `DateTime.UtcNow`.

## 6. Test plan

- `Domain.UnitTests`: `SlaClock_DueTimes_ComputesWarnAt`, `MarkSlaWarned_FirstCall_RaisesEventNoHistory`, `MarkSlaWarned_Twice_IsNoOp`, `MarkSlaBreached_FirstCall_RaisesSlaBreachedEvent`, `ChangeSeverity_ClearsWarnedAndRecomputesWarnAt`.
- `Application.UnitTests/Sla/`: `Evaluate_PastWarnAt_WarnsOnce`, `Evaluate_PastDue_BreachesAndSkipsWarning`, `Evaluate_ResolvedIncident_Ignored`, `Evaluate_OnHold_StillCounts`, `Evaluate_ConcurrencyConflict_SkipsAndContinues` (with `FakeTimeProvider`).
- `Worker.UnitTests`: `SlaMonitorJob_LockHeldElsewhere_DoesNotEvaluate`, `SlaMonitorJob_EvaluationThrows_LoopContinues`.
- `Api.IntegrationTests/Sla/` `[Trait("Category","Integration")]` (Testcontainers SQL + Redis): `Monitor_TwoInstances_OnlyOneEvaluates`, `Monitor_ManyTicks_ExactlyOneWarningRow`, `Monitor_Breach_SetsFlagHistoryAndOutboxInOneSave`, `Monitor_SeverityChangeAfterWarning_AllowsNewWarning`, `Monitor_PolicyChange_DoesNotMoveExistingWarnAt`; opt-in plan test `SlaCandidatesQueryPlanTests` (index seek on 50k rows).

## 7. Out of scope

- Recipients and email content -> 12_03.
- Broadcasting / cache refresh of Worker-made changes -> 12_04.
- In-app notifications (`SlaWarning`/`SlaBreached` rows in `Notifications`) -> BE-14; Teams/Slack -> BE-13.
- Acknowledgement-SLA breach flag (OQ 2).
- Pausing the clock on `OnHold`; business hours.

## 8. Open questions

1. **Warning basis and storage.** DATABASE.md has no warn column. Proposed: store `SlaWarnAt` at the same time as the due times (so policy edits never move it) plus `SlaWarnedAt` as the dedup marker. Alternative: derive the warn time from `ResolveDueAt` and the *current* policy percent (no `SlaWarnAt`), which would move with policy edits and conflicts with the "no retroactive change" spirit. Backfill of existing open incidents: from the current policy (proposed) or leave null (never warned)?
2. **What is "breach"?** The domain doc says `MarkSlaBreached` flags the *resolve* SLA; `AckDueAt` passing unacknowledged is not covered by BE-12's wording. Proposed: resolve only.
3. **History for the warning.** Every field change appends history, but a warning is an internal marker. Proposed: no history row for `SlaWarnedAt` (it is not user-visible); history only for `IsSlaBreached` (exists). Confirm.
4. **Outbox type names.** BACKEND-SPECS says types `SlaWarning`, `SlaBreached`; the outbox maps `Type` to the event class name. Proposed: name the events `SlaWarning` and `SlaBreached` (rather than `IncidentSla...`) to match, in `Incidents/Events/`.
5. **Clock and `OnHold`.** Wall-clock, no pause (matches `SlaPolicy` "24x7"). Confirm.
6. **Warn and breach in one tick** (monitor was down): breach only, proposed.

## 9. Decisions (2026-10-19)

- Open questions 1-6 accepted as proposed. OQ 1 backfill counts back from `ResolveDueAt` with the current policy (the real start time is not stored), so open incidents already past their warn time get one warning on the first tick after deployment.
- Handler tests live in `Api.IntegrationTests/Sla/` (real SQL, `FakeTimeProvider`) instead of `Application.UnitTests`, because the handler queries `IAppDbContext` and the DbContext is not mocked. The warn/breach decision is a pure `SlaEvaluation` helper with Domain unit tests.
- One `SaveChangesAsync` per incident (outbox rows and history in the same transaction; a failure is isolated). Batches use keyset paging on `(ResolveDueAt, Id)` so a failing incident is not re-read in the same run.
- `IAppDbContext` exposes `ChangeTracker` (core EF, ADR 0001) so the handler can clear a failed incident's state.
- The `sla.evaluate` span is started by the Worker job, keeping the handler free of Infrastructure types.
