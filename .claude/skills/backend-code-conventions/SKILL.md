---
name: backend-code-conventions
description: Engineering conventions for the IncidentHub .NET/C# backend. Use when implementing, refactoring or reviewing any backend C# code (handlers, domain entities, controllers, EF Core configurations, migrations, Worker jobs, tests) to keep it readable, maintainable, testable and consistent with the existing codebase — applying SOLID, KISS, DRY, YAGNI, design patterns and ACID transaction rules only where they genuinely help.
---

# Backend code conventions

`CLAUDE.md` defines **what** this codebase requires (layers, workflow rules, outbox, security, testing). This skill
defines **how to decide** when writing or reviewing code. If the two conflict, `CLAUDE.md`, the ADRs in `docs/adr/`
and the specs win.

The goal is code that a teammate can read once and change safely. Principles are tools for that goal, not a checklist.

---

## 1. Before writing or changing code

1. **Read before you write.** Open the nearest existing equivalent first and copy its shape: a new command → read a
   sibling use case folder (e.g. `Application/Apps/CreateApp/`); a new EF mapping → read a neighbouring
   `Persistence/Configurations/*Configuration.cs`; a new test → read the tests next to it.
2. **Find what already exists** before adding a helper, exception, DTO or extension: Grep `Application/Common`,
   `Domain/Common`, `*Rules.cs`, `*Queries.cs`, `*ReferenceChecks.cs`, the test builders and fixtures.
3. **Understand why the existing design is that way** (spec, ADR, comment, git log) before proposing to change it.
   Consistency beats local perfection: a slightly worse pattern used everywhere is better than two patterns.
4. **Scope:** change only what the task needs. Note unrelated problems in your reply; don't fix them in the same change.

## 2. Established patterns in this codebase (follow, don't reinvent)

| Concern | The way it's done here | Don't |
|---|---|---|
| Use case | One folder: `<UseCase>Command/Query` + `Handler` + `Validator` (MediatR) | Service classes with many methods |
| Data access | Handlers use `IAppDbContext` directly (ADR 0001) | Add a generic `IRepository<T>` / unit-of-work wrapper over EF |
| Shared query/rule logic | Small static helpers per feature: `AppQueries.Project`, `AppReferenceChecks`, `TeamRules` | Base handler classes, inheritance hierarchies |
| Business rules / invariants | Domain methods on the aggregate (`Incident.TransitionTo`, `MonitoredApp.Update`) | `if` rules in controllers, handlers or EF configs |
| Errors | Throw `NotFoundException`, `ConflictException`, `ForbiddenException`, `DomainException`; `ProblemDetailsMiddleware` maps them | Build `ProblemDetails`/status codes by hand; return `null` to mean "not found" |
| Validation | FluentValidation in the `Validator`, run by `ValidationBehavior` | Validate the same input again in the handler/controller |
| Reads | `AsNoTracking()` + `Select` projection to DTO | Load aggregates then map in memory |
| Side effects | Domain events → `OutboxMessage` in the same `SaveChangesAsync`; SignalR after commit | Call email/Teams/HTTP from a handler |
| Time | Inject `TimeProvider`; domain receives `at` | `DateTime.UtcNow`, `DateTimeOffset.Now` |
| DI-only classes | `sealed` + primary constructor | Field + constructor boilerplate |
| DTOs / commands | `sealed record` | Mutable classes with setters |

## 3. Principles — apply with judgment

For each principle: apply it when it removes a real problem you can name; skip it when it only adds structure.
If you deliberately choose the simpler option over a principle, say so in one line in your reply (e.g. "Kept the
check inline instead of extracting a strategy — there's one implementation and no second one planned").

### SOLID
- **Single responsibility:** one reason to change per class. A handler orchestrates one use case; the domain
  enforces rules; the controller maps HTTP. A 40-line handler doing exactly one use case is fine — don't split it
  into "services" to make it shorter.
- **Open/closed:** extend through the existing seams (new MediatR handler, new pipeline behavior, new
  `IEntityTypeConfiguration<T>`). Don't pre-build plug-in points for variations nobody has asked for.
- **Liskov:** subclasses must honour the base contract. Rare here — prefer composition; classes are `sealed` by default.
- **Interface segregation:** small, purpose-named interfaces (`ICurrentUser`, `IAppTeamReader`, `IIdempotencyStore`).
- **Dependency inversion:** add an interface in `Application/Abstractions` **only** for something the Application
  layer must not reference directly (infrastructure, I/O, external services, clock) or must fake in tests.
  Don't add an interface for a class with one implementation that's pure logic — test it directly.

```csharp
// Bad — interface + DI registration for pure logic with one implementation
public interface IFingerprintCalculator { string Compute(Guid appId, string title); }
public sealed class FingerprintCalculator : IFingerprintCalculator { ... }

// Good — a static pure function, tested directly (this is how `IncidentFingerprint` is done)
public static class IncidentFingerprint { public static string Compute(Guid appId, string title) => ...; }
```

### KISS
- The most direct code that meets the spec. Plain `if`/`switch` over a pattern until there are real variants.
- No generic "framework" code (`BaseHandler<TCommand, TResult, TEntity>`) for two use cases.
- Readable LINQ over clever LINQ; split a query into named steps if it doesn't fit on a screen.

### DRY
- Remove duplication of **knowledge** (a business rule, a query shape, a validation limit), not of similar-looking
  lines. Two handlers that both call `SaveChangesAsync` are not duplication.
- Rule of three: inline it the first and second time; extract on the third, or earlier if it's a business rule that
  must never diverge (e.g. the DTO projection used by both GET and POST → `AppQueries.Project`).
- Shared limits come from one place — constants on the entity (`Team.NameMaxLength`) — used by validators (`TeamRules`) and
  EF configurations alike.

### YAGNI
- Build what the current spec requires. Anything a later BE item schedules goes to
  `docs/implementation/deferred-items.md`, not into speculative code.
- No unused parameters, options, flags, extension points or "for later" interfaces.
- No new package, layer, project or pattern without a concrete need stated in the change; architecture-level changes
  (broker, new store, new service) need an ADR (`CLAUDE.md` → Don'ts).

### Design patterns
Use a pattern when it names something the code already needs:

| Pattern | Use here when | Avoid when |
|---|---|---|
| Mediator / CQRS (MediatR) | Every use case — already established | Inventing a second dispatch mechanism |
| Pipeline behavior | Cross-cutting for all requests (validation, logging) | Logic specific to one use case |
| State machine | Incident workflow — already in `Incident.TransitionTo` | Duplicating transition rules elsewhere (FE map aside) |
| Domain events + outbox | Side effects of a state change | Synchronous notifications |
| Strategy | ≥ 2 real, swappable behaviours selected at runtime (e.g. notification channels) | One implementation "in case" |
| Factory method | Creating a valid aggregate (`MonitoredApp.Create`) | Wrapping `new` with no invariants |
| Repository / Unit of Work | Not used — `DbContext` already is both (ADR 0001) | Adding it on top of EF |

### Clean code
- Names say intent in domain language (`CodeTakenAsync`, `EnsureReferencesExistAsync`), not types (`DoCheck`, `Helper`).
- Methods do one thing at one level of abstraction; guard clauses over deep nesting.
- Comments explain **why** (a race, a SQL Server quirk, a spec decision), never restate the code. Match the
  surrounding comment density.
- No magic numbers/strings for rules — named constants next to their owner.
- Fail loudly: throw the specific exception; don't swallow, don't return sentinel values.
- `async` all the way, `CancellationToken` passed to every async call; structured log templates, no interpolation.
- Nullable-clean, warnings-as-errors: fix the warning, don't `!` or `#pragma` it away without a comment saying why.

## 4. ACID — database operations and transaction boundaries only

ACID is about how a use case touches SQL Server, not a general style rule.

- **Atomicity — one use case, one `SaveChangesAsync`.** The aggregate change, its `IncidentHistory` rows and its
  `OutboxMessage` rows are saved together. Never call `SaveChangesAsync` twice in one handler to "save then notify";
  never send anything external before the commit.
- **Consistency — invariants live in two places on purpose:** the domain (rules) and the database (constraints:
  unique `Code`, filtered unique `Fingerprint`, FKs, `rowversion`). A pre-check in the handler gives a friendly error,
  but the **constraint is the real guard** — always handle the race:

  ```csharp
  // Good — pre-check for a nice message, unique index for correctness (CreateAppHandler)
  if (await AppReferenceChecks.CodeTakenAsync(db, request.Code, exceptId: null, ct)) throw new ConflictException(...);
  db.Applications.Add(app);
  try { await db.SaveChangesAsync(ct); }
  catch (DuplicateKeyException) { throw new ConflictException(...); } // lost a concurrent race

  // Bad — check-then-insert with no constraint/catch: two concurrent requests both pass the check
  ```
- **Isolation — optimistic concurrency by default:** `RowVersion` + `If-Match`; a `DbUpdateConcurrencyException` →
  409. Don't add explicit transactions or raise isolation levels to "be safe". Use explicit locking hints only where
  the design calls for them (Worker outbox claim: `UPDLOCK, READPAST`; single-instance jobs: Redis lock).
- **Durability — commit before announcing.** SignalR broadcasts and outbox dispatch happen after the commit;
  dispatch is retried and senders are idempotent by message `Id`.
- **Explicit transactions (`BeginTransactionAsync`)** only when one use case genuinely needs multiple round trips
  that must succeed or fail together (e.g. claim + update in the Worker). Otherwise the implicit transaction of a
  single `SaveChangesAsync` is the right boundary.
- Never mix a DB write with a non-transactional store (Redis, Blob) as if they were atomic. Write SQL first, make the
  other side derivable or retryable (cache is invalidated/recomputed; blobs are orphan-tolerant).
- Migrations: schema changes are reversible (`Down`), applied by the pipeline, never `Database.Migrate()` at startup.

## 5. Testability

- Design so the logic is testable without mocks: pure domain methods with `at`/actor passed in, static pure helpers.
- Fake only boundaries (`ICurrentUser`, `IAppTeamReader`, `TimeProvider`, future file/notification senders) with NSubstitute. Never mock
  `DbContext` — use the Testcontainers fixtures.
- If something is hard to test, that's a design signal (hidden clock, static I/O, logic in a controller) — fix the
  design rather than adding test-only hooks.
- Tests follow `CLAUDE.md` → Testing (xUnit, FluentAssertions, `Method_Scenario_ExpectedResult`, AAA).

## 6. When reviewing code

Report **meaningful** issues, ordered by impact, each with a location, why it matters and a practical fix:

1. Correctness / data integrity — broken transaction boundary, missing race handling, bypassed domain method,
   missing `CancellationToken`, wrong status code mapping.
2. Architecture violations — layer references, logic in controllers/hubs/configs, entities returned from endpoints.
3. Maintainability — duplicated business rules, misleading names, a needless abstraction, an unexplained workaround.
4. Testability gaps — untestable design, missing tests for new behaviour.

Rules for reviews:
- Don't flag style that `dotnet format` handles or that matches the surrounding code.
- Don't recommend a pattern or abstraction without naming the concrete problem it solves here.
- Don't propose refactoring code the change didn't touch; list it separately as "outside scope" if it's important.
- Removing an unnecessary abstraction is a valid recommendation.

## 7. Quick decision examples

| Situation | Good decision | Bad decision |
|---|---|---|
| Second handler needs the same DTO projection | Extract to the feature's `*Queries.Project` | Copy the `Select` (drift) |
| Create and update both validate `Name` length 200 | Shared `TeamRules.Apply` using `Team.NameMaxLength` | Literal `200` in each validator |
| New use case needs current user | Inject `ICurrentUser` | Read `HttpContext` in the handler |
| "We might support Slack later" | Implement Teams only; note it in deferred items if a spec says so | `INotificationChannelFactory` with one channel |
| Need to send email after resolve | Domain event → outbox row in the same save | `await emailSender.SendAsync` in the handler |
| Handler grows past one screen | Move rules into the aggregate; extract a named query helper | Split into `XService`, `XManager`, `XHelper` |
| Unique business key | Unique index + catch `DuplicateKeyException` → 409 | Pre-check only |
| Need a library for one small function | Write the 10 lines | Add a NuGet package |
