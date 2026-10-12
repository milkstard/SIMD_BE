# Spec 05_01: SLA clock & transactional outbox

> Status: **Implemented** · Source: BE-05 in docs/BACKEND-SPECS.md · Part 01 of 02

## 1. Purpose & scope

Build the two pieces the report path (05_02) needs underneath it, each testable without an endpoint:

- **SLA due times** — `SlaClock` computes `AckDueAt`/`ResolveDueAt` from a severity's policy. Until BE-12 the policy comes
  from configuration with built-in defaults.
- **Transactional outbox** — every domain event raised by a tracked aggregate is written as an `OutboxMessage` row in the
  **same `SaveChangesAsync`** (and so the same transaction) as the change that raised it.

Pulled in from `deferred-items.md`: "`SlaClock` + default SLA policy" (04_01) and "`OutboxMessage` entity/table; domain
events → outbox rows in the same `SaveChangesAsync`" (04_01, 04_02).

No endpoint, no handler, no dispatcher.

## 2. Dependencies

- BE-01 (`AppDbContext`, Worker host), BE-03 (catalog migrations).
- BE-04: **04_01** (`AggregateRoot.DomainEvents`/`ClearDomainEvents`, `Incident.SetSlaDueTimes`, events `IncidentReported`,
  `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated`), **04_02** (`AppDbContext.SaveChangesAsync` override, `AddIncidents` migration).
- No earlier parts.

## 3. Design

### Domain (`IncidentHub.Domain`)
`Incidents/Sla/`
- `SlaPolicy` — sealed record `(Severity Severity, int AckMinutes, int ResolveMinutes, int EscalateAtPercent)`; ctor guards:
  minutes > 0, `ResolveMinutes >= AckMinutes`, `EscalateAtPercent` 1–99.
- `SlaClock` (static, pure — Domain has no clock): `SlaDueTimes DueTimes(SlaPolicy policy, DateTimeOffset from)` →
  `record struct SlaDueTimes(DateTimeOffset AckDueAt, DateTimeOffset ResolveDueAt)`. Wall-clock 24x7 (no business hours), result in UTC.
  `Incident.SetSlaDueTimes` (04_01) and `ChangeSeverity` (BE-07) take its output.

### Application (`IncidentHub.Application`)
- `Abstractions/ISlaPolicyProvider` — `Task<SlaPolicy> GetAsync(Severity severity, CancellationToken ct)`.
  Async on purpose: BE-12 replaces the implementation with a `SlaPolicies` table read without changing callers.
- `Common/Sla/SlaDefaultsOptions` — section `Sla:Defaults`, one entry per severity (`AckMinutes`, `ResolveMinutes`, `EscalateAtPercent`).
  Built-in values used when the section or an entry is missing (decision OQ 1):

  | Severity | Ack | Resolve | Escalate at |
  |---|---|---|---|
  | Critical | 15 min | 240 min (4 h) | 80 % |
  | High | 60 min | 480 min (8 h) | 80 % |
  | Medium | 240 min | 4,320 min (72 h) | 80 % |
  | Low | 480 min | 7,200 min (120 h) | 80 % |

- `Common/Sla/SlaDefaultsOptionsValidator : IValidateOptions<SlaDefaultsOptions>` — same rules as the `SlaPolicy` guards;
  registered with `ValidateOnStart()` so a bad config stops the API/Worker at startup.
- `IAppDbContext` is **not** extended: handlers never touch the outbox (CLAUDE.md: domain events are converted, not written by hand).

### Infrastructure (`IncidentHub.Infrastructure`)
- `Outbox/OutboxMessage` — persistence type, not a domain entity: `Id` (Guid), `Type`, `Payload`, `OccurredAt`, `ProcessedAt?`, `Attempts`, `Error?`.
  Factory `OutboxMessage.From(IDomainEvent e, JsonSerializerOptions json)`: `Type` = event type name (e.g. `IncidentReported`, ≤ 50),
  `Payload` = System.Text.Json of the concrete event (camelCase, enums as strings), `OccurredAt` = `e.OccurredAt`.
- `Persistence/Configurations/OutboxMessageConfiguration` (table `OutboxMessages`, per DATABASE.md): `Type nvarchar(50)` not null, `Payload nvarchar(max)` not null,
  `OccurredAt`/`ProcessedAt datetimeoffset`, `Attempts int` default `0`, `Error nvarchar(max)` null, `Id` `ValueGeneratedNever()`.
  Index `IX_OutboxMessages_Unprocessed` on `ProcessedAt` filtered `[ProcessedAt] IS NULL` (include `OccurredAt` for the BE-10 ordered poll — see OQ 3).
- `AppDbContext`:
  - `DbSet<OutboxMessage> OutboxMessages` (on the concrete context only).
  - `SaveChangesAsync`/`SaveChanges` override, in this order: append-only guard (04_02) → collect events from `ChangeTracker.Entries<AggregateRoot>()` →
    add one `OutboxMessage` per event → `base.SaveChanges*` (one transaction) → on success `ClearDomainEvents()` on those aggregates.
    On failure events stay on the aggregate and the outbox entries are detached, so a retry does not add them twice.
    The "after success" step is the hook BE-09 extends for post-commit SignalR dispatch.
  - Existing unique-violation translation (04_02) is unchanged.
- `Sla/ConfiguredSlaPolicyProvider : ISlaPolicyProvider` — reads `IOptions<SlaDefaultsOptions>`.
- `DependencyInjection.AddInfrastructure`: bind + validate `SlaDefaultsOptions`, register `ISlaPolicyProvider` (singleton).
- Migration `AddOutboxMessages` (`dotnet ef migrations add AddOutboxMessages -p src/IncidentHub.Infrastructure -s src/IncidentHub.Api`), with `Down`.

### Api / Worker
- `appsettings.json` (Api and Worker): add an `Sla:Defaults` section with the table above (documentation of the defaults; not a secret).
- No code changes otherwise.

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `contracts/swagger.json` unchanged. `docs/DATABASE.md` `OutboxMessages` already matches; add the index name
`IX_OutboxMessages_Unprocessed` to its Indexes line.

## 5. Acceptance criteria

- [ ] `SlaClock.DueTimes` returns `from + AckMinutes` / `from + ResolveMinutes` for every severity, in UTC, for the default table above.
- [ ] Values in `Sla:Defaults` override the built-in defaults per severity; a missing entry falls back to the built-in value.
- [ ] Invalid config (minutes ≤ 0, resolve < ack, percent outside 1–99) fails host start with an `OptionsValidationException` naming the field.
- [ ] Saving a newly reported `Incident` writes exactly one `OutboxMessages` row with `Type = IncidentReported`, a JSON payload that deserializes back to the event, `ProcessedAt = null`, `Attempts = 0`.
- [ ] The outbox row is written in the **same transaction** as the incident: when the save fails (forced failure, e.g. duplicate open fingerprint), neither the incident, its history nor the outbox row exists (rollback test).
- [ ] Domain events are cleared only after a successful save; after a failed save they are still on the aggregate and no outbox entity remains tracked.
- [ ] Events from other aggregates/changes (`IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated`) also produce one row each (generic, not report-specific).
- [ ] `AddOutboxMessages` applies on top of `AddIncidents` and on an empty database; `Down` reverts it; schema (columns, nullability, default, filtered index) matches DATABASE.md.
- [ ] No handler, controller or Application type references `OutboxMessage`; build (warnings as errors) and `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Domain.UnitTests/Incidents/Sla/`
- `SlaClockTests`: `DueTimes_EachSeverity_AddsPolicyMinutes` (`[Theory]` over the 4 defaults), `DueTimes_NonUtcOffset_ReturnsUtc`.
- `SlaPolicyTests`: `Ctor_ResolveLessThanAck_Throws`, `Ctor_NonPositiveMinutes_Throws`, `Ctor_PercentOutOfRange_Throws`.

`tests/IncidentHub.Application.UnitTests/Common/Sla/`
- `SlaDefaultsOptionsValidatorTests`: `Validate_Defaults_Succeeds`, `Validate_ResolveLessThanAck_Fails`, `Validate_PercentOutOfRange_Fails`.

`tests/IncidentHub.Api.UnitTests/` (or Infrastructure-facing unit tests where they already live)
- `ConfiguredSlaPolicyProviderTests`: `GetAsync_NoConfig_ReturnsBuiltInDefault`, `GetAsync_ConfiguredSeverity_ReturnsConfiguredValues`.

`tests/IncidentHub.Api.IntegrationTests/Outbox/OutboxPersistenceTests` — `[Trait("Category","Integration")]`, shared SQL Server container fixture:
- `SaveIncident_Reported_WritesOutboxRowInSameSave`
- `SaveIncident_SaveFails_RollsBackIncidentHistoryAndOutbox`
- `SaveIncident_Success_ClearsDomainEvents`, `SaveIncident_Failure_KeepsDomainEventsAndDetachesOutbox`
- `TransitionIncident_Persisted_WritesIncidentTransitionedRow`
- `Migrate_AddOutboxMessages_UpAndDown`, `Schema_OutboxMessages_MatchesDatabaseDoc`

## 7. Out of scope

- `OutboxDispatcher`, senders, Polly, claiming with `UPDLOCK, READPAST` → BE-10.
- Post-commit SignalR dispatch of domain events → BE-09 (extends the post-save hook added here).
- `SlaPolicies` table, `GET/PUT /sla-policies`, replacing `ConfiguredSlaPolicyProvider` → BE-12.
- Using `EscalateAtPercent` (warnings/breach) → BE-12 `SlaMonitor`.
- Report endpoint and handler → 05_02.

## 8. Open questions — resolved

Decided with the user on 2026-10-12:

1. No doc gives default SLA values. Decided: the table in §3 (Critical 15 m / 4 h, High 1 h / 8 h, Medium 4 h / 72 h, Low 8 h / 120 h), wall-clock 24x7, escalate at 80 %.
2. Source of the defaults until BE-12: configuration `Sla:Defaults` with built-in fallback, validated on start; `SlaClock` stays a pure Domain calculator.

3. Outbox filtered index also `INCLUDE (OccurredAt)` for BE-10's ordered poll; `DATABASE.md` updated (decided 2026-10-12).
4. BE-10 lists `OutboxMessage` under its own requirements; this part creates the table earlier because BE-05 requires the outbox row. BE-10 keeps only the dispatcher.
5. `Microsoft.Extensions.Options` is added to Application for `SlaDefaultsOptionsValidator` (allowed by the layering guard; decided 2026-10-12).

## 9. Implementation notes

- `SlaDefaultsOptions` initialises each severity entry with its built-in default, so the configuration binder only overrides supplied values and a missing entry falls back without extra code.
- `SlaPolicy` is a record with a guarded constructor (not positional) so every instance, including ones built by `ToPolicy`, is valid.
- `AddSlaPolicies` is a separate extension (called from `AddInfrastructure`) so it can be tested without connection strings.
- `AppDbContext` stages outbox rows per save, clears events only in `OnSaved`, and detaches the staged rows when the save throws (including the unique-violation translation path).
- Review follow-ups: `AppDbContext` sync and async saves share `BeforeSave`/`TranslateUniqueViolation` (the sync path now also maps unique violations); `SlaPolicy.MinEscalatePercent`/`MaxEscalatePercent` are shared with the validator; `ConfiguredSlaPolicyProvider` uses `IOptions` (not `IOptionsMonitor`) so only start-validated values are used; `DomainEventTypeTests` guards unique, ≤ 50-char event names for BE-10's type mapping.
