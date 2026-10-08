# CLAUDE.md — IncidentHub Backend (ASP.NET Core / .NET 8)

## What this is
Backend for the Service Incident & Monitoring Dashboard: a web app where users **report**, **track**
and **monitor** application incidents. Two processes share one codebase:

- `IncidentHub.Api` — REST API (`/api/v1`) + SignalR hub (`/hubs/incidents`). Handles every user action.
- `IncidentHub.Worker` — BackgroundService: outbox dispatcher (notifications), SLA timer, nightly stats.

Stores: **SQL Server** (all incident data, EF Core 8), **Redis** (dashboard cache, SignalR backplane,
locks), **Blob Storage** (attachments). There is NO time-series DB and NO message broker in v1.

## Reference docs
Imported (loaded into context every session):
- Database schema — tables, columns, indexes, schema rules. Consult before touching entities, EF configurations or migrations: @docs/DATABASE.md
- API contracts — endpoints, DTOs, enums, errors, SignalR events. Update it in the same change as any endpoint/DTO change: @docs/API-CONTRACT.md
- Architecture decisions (ADRs) — one file per decision that changes the architecture; these override the sections below if they conflict.
  Add an `@docs/adr/<NNNN-title>.md` import line here whenever a new ADR is created (imports don't support globs or directories):
@docs/adr/0001-ef-core-in-application.md

Reference only (NOT imported — read on demand when you need the original rationale, requirements or trade-offs):
- `docs/System Design Service Incident & Monitoring Dashboard.md`

## Commands
```bash
docker compose up -d                                  # SQL Server, Redis, Azurite
dotnet build
dotnet test                                           # all tests (integration tests need Docker)
dotnet test --filter Category!=Integration            # fast unit tests only
dotnet run --project src/IncidentHub.Api
dotnet run --project src/IncidentHub.Worker
dotnet ef migrations add <Name> -p src/IncidentHub.Infrastructure -s src/IncidentHub.Api
dotnet format                                         # run before committing
```
After changing any endpoint or DTO, regenerate the OpenAPI spec (`swagger.json` in `/contracts`)
— the frontend generates its TypeScript types from it and CI fails on an unexpected diff.
Run `scripts/generate-openapi.ps1` (uses fixed placeholder config so the output is stable).

Local config (user-secrets, never committed): `AzureAd:TenantId`, `AzureAd:ClientId`, `Authorization:GroupRoleMap:<group-id>` = role,
`ConnectionStrings:Sql`, `ConnectionStrings:Redis`. The API refuses to start without the Entra settings. See `.claude/specs/entra-id-authorization.md`.

## Solution layout (Clean Architecture — respect the dependency direction)
```
src/
  IncidentHub.Domain/          # Entities, value objects, workflow state machine, domain events. NO dependencies.
  IncidentHub.Application/     # MediatR commands/queries, FluentValidation, DTOs, interfaces (IFileStore, INotificationSender)
  IncidentHub.Infrastructure/  # EF Core DbContext + configs, Redis, Blob, email/Teams senders, outbox
  IncidentHub.Api/             # Controllers, IncidentHub (SignalR), auth policies, ProblemDetails, DI wiring
  IncidentHub.Worker/          # OutboxDispatcher, SlaMonitor, DailyStatsJob
tests/
  Domain.UnitTests, Application.UnitTests, Api.UnitTests, Api.IntegrationTests (Testcontainers)
```
Domain → nothing. Application → Domain. Infrastructure → Application. Api/Worker → all.
Never reference EF Core, ASP.NET or Infrastructure types from Domain or Application.

## Core domain rules (do not bypass)
- **All status changes go through `Incident.TransitionTo(newStatus, actor, note)`.** Never set `Status`
  directly. The state machine is the single source of truth:

  | From | Allowed to | Who |
  |---|---|---|
  | New | Triaged, Closed (duplicate/invalid) | Responder |
  | Triaged | InProgress, OnHold | Responder |
  | InProgress | Resolved, OnHold | Assignee, Responder |
  | OnHold | InProgress | Assignee, Responder |
  | Resolved | Closed, Reopened | Reporter, Responder |
  | Reopened | InProgress | Responder |

- Every change to an incident appends `IncidentHistory` rows (field, old, new, actor, time). History is append-only.
- SLA due times (`AckDueAt`, `ResolveDueAt`) are computed by `SlaClock` on create and on severity change — never in controllers.
- Application status is **derived**, never stored by users: open Critical in Production → Outage;
  open High → Degraded; else Operational. Recompute via `AppStatusCalculator` and cache in Redis.
- `Incident.Number` (e.g. `INC-1042`) comes from a SQL `SEQUENCE`. URLs use Number; internal keys use `Id` (Guid).

## Patterns to follow
- **One use case = one MediatR request + handler + validator** in `Application/Incidents/<UseCase>/`.
  e.g. `ReportIncident/ReportIncidentCommand.cs`, `...Handler.cs`, `...Validator.cs`.
- Controllers are thin: map request → command → `ISender.Send` → map result. No business logic.
- **Outbox**: handlers never call email/Teams/SignalR-to-external directly. Domain events are converted to
  `OutboxMessage` rows and saved in the **same `SaveChangesAsync`** as the incident. The Worker dispatches them.
- SignalR broadcasts from the API happen **after commit** (domain event dispatcher post-save hook).
  Use the typed hub `Hub<IIncidentClient>`; never use string method names.
- Concurrency: `Incident.RowVersion` + `If-Match` header. Stale write → `409 Conflict`.
- Create endpoints honour the `Idempotency-Key` header.
- Errors: RFC 7807 `ProblemDetails` with `traceId`. Validation → 400/422, forbidden transition → 403,
  invalid/stale change → 409. Use the domain `Result<T>`/exceptions mapped in `ProblemDetailsMiddleware`;
  don't build error responses by hand.
- Queries that feed lists/dashboard use `AsNoTracking()` and project straight to DTOs (`Select`), never load aggregates.
- Async all the way; pass `CancellationToken` through every handler and EF call.

## Security rules
- Auth: Entra ID / OIDC JWT. Roles: Reporter, Responder, TeamLead, Admin (mapped from groups).
- Use policy-based authorization (`[Authorize(Policy = Policies.CanTransitionIncident)]`) **plus** a resource
  check that the user belongs to the incident's application team.
- Internal comments (`IsInternal = true`) must never reach Reporters — not in REST responses, not in SignalR
  (`CommentAdded` goes to the responders group only).
- Sanitize rich-text `Description` and comment bodies with HtmlSanitizer on write.
- Attachments: validate type + size (≤ 25 MB), block download until `ScanStatus = Clean`, serve via
  SAS link with 5-minute expiry. Never stream blobs through the API.
- No secrets in appsettings; use user-secrets locally and Key Vault in Azure.

## Data access
- EF Core configurations live in `Infrastructure/Persistence/Configurations/` (one `IEntityTypeConfiguration<T>` per entity). No data annotations on domain entities.
- Migrations are applied by the pipeline (migration bundle), **never** `Database.Migrate()` at startup.
- Keep the key indexes: `(Status, Severity) INCLUDE (ApplicationId, AssigneeId)`, `(ApplicationId, CreatedAt)`,
  full-text on `Title`, `Description`. Check query plans before adding new list filters.
- Dashboard endpoints read Redis first, fall back to SQL; cache TTL 5 s.

## Worker
- `OutboxDispatcher`: polls every 2 s, claims a batch of 50 with `UPDLOCK, READPAST`, sends, marks
  `ProcessedAt`; failures increment `Attempts` with exponential back-off. Senders must be idempotent by message Id.
- `SlaMonitor`: runs every minute; warns assignee at the policy's percent; on breach sets `IsSlaBreached`
  and escalates to the application's escalation contact. Only one instance runs at a time (Redis lock).
- Outbound calls use Polly (timeout, retry with jitter, circuit breaker per channel).

## Testing
- Framework: **xUnit** for all test projects (`[Fact]`/`[Theory]`; shared setup via `IClassFixture`/`ICollectionFixture`,
  e.g. Testcontainers fixtures). Tag integration tests with `[Trait("Category", "Integration")]`.
- Domain: every allowed AND forbidden transition per role, SLA calculations, app status rules. Pure unit tests.
- Application: handlers with NSubstitute fakes; validators with FluentValidation's `TestValidate`.
- Integration: `WebApplicationFactory` + Testcontainers (SQL Server, Redis). Cover auth policies, 409 on stale
  RowVersion, outbox row written in the same transaction, SignalR broadcast received by a test client.
- Naming: `Method_Scenario_ExpectedResult`. Arrange/Act/Assert. FluentAssertions.
- New feature = tests in the same PR. Don't mock `DbContext`; use the container.

## Code style
- C# 12, nullable enabled, warnings as errors. File-scoped namespaces. `sealed` by default for classes.
- Records for DTOs/commands. Primary constructors for DI-only classes.
- Use `TimeProvider` (inject it) — never `DateTime.Now`/`UtcNow` directly. Store all times as UTC (`DateTimeOffset`).
- Logging via `ILogger<T>` with structured templates (`"Incident {IncidentNumber} resolved"`), no string interpolation.
- OpenTelemetry is configured in `Api/Program.cs` and `Worker/Program.cs`; add spans for new outbound calls.

## Don'ts
- Don't add a message broker, microservice, or new database without an ADR in `/docs/adr`.
- Don't put logic in controllers, SignalR hubs, or EF configurations.
- Don't change the workflow table above without updating the frontend `nextStatuses` map and the domain tests.
- Don't return EF entities from endpoints — always DTOs.
- Don't send notifications synchronously from a request.
