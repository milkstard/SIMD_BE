# Completed items

Items moved here from [`deferred-items.md`](deferred-items.md) once an implementation delivers them.

| Item | Source spec | Completed by | Commit | Notes |
|---|---|---|---|---|
| `SlaClock` + default SLA policy (config-backed `ISlaPolicyProvider`, `Sla:Defaults`) | 04_01 | 05_01 | uncommitted | Setting the due times at creation stays with 05_02 |
| `OutboxMessage` entity/table; domain events → outbox rows in the same `SaveChangesAsync` | 04_01, 04_02 | 05_01 | uncommitted | Migration `AddOutboxMessages`; events cleared only after a successful save |
