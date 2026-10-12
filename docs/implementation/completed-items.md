# Completed items

Items moved here from [`deferred-items.md`](deferred-items.md) once an implementation delivers them.

| Item | Source spec | Completed by | Commit | Notes |
|---|---|---|---|---|
| `SlaClock` + default SLA policy (config-backed `ISlaPolicyProvider`, `Sla:Defaults`) | 04_01 | 05_01 | 057a417 | Setting the due times at creation stays with 05_02 |
| `OutboxMessage` entity/table; domain events → outbox rows in the same `SaveChangesAsync` | 04_01, 04_02 | 05_01 | 057a417 | Migration `AddOutboxMessages`; events cleared only after a successful save |
| Set `AckDueAt`/`ResolveDueAt` at creation using `SlaClock` | 04_01 | 05_02 | uncommitted | Report handler calls `SlaClock.DueTimes` with the configured policy |
| Team-membership resource check on incident endpoints (report half) | 04_01 | 05_02 | uncommitted | `POST /incidents` requires membership of the application's team; the BE-07 half stays deferred |
