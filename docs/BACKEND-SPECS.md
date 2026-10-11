# IncidentHub — Backend Specs (Spec-Driven Development)

_Source: System Design — Service Incident & Monitoring Dashboard._
_ASP.NET Core (.NET 8), Clean Architecture, CQRS/MediatR, EF Core 8, SQL Server, Redis, Blob Storage._

Each spec below is a self-contained unit of work: **Context → Requirements → Acceptance Criteria → Dependencies**.
Implement in ID order within a phase; phases are sequential.

---

## PHASE 1 — MVP

### BE-01 · Solution scaffold
**Context:** Nothing exists yet. Need the Clean Architecture skeleton every other spec builds on.
**Requirements:**
- Solution `IncidentHub.sln` with 5 projects: `Domain`, `Application`, `Infrastructure`, `Api`, `Worker`
- Dependency direction: `Domain` ← `Application` ← `Infrastructure` ← `Api`/`Worker` (never reversed)
- `docker-compose.yml`: SQL Server, Redis, Azurite (blob emulator)
- Serilog + OpenTelemetry wired in both `Api/Program.cs` and `Worker/Program.cs`

**Acceptance Criteria:**
- [x] `dotnet build` succeeds with zero project-reference violations
- [x] `docker compose up -d` brings up all three containers healthy
- [x] A log line and a trace span are emitted on API startup
**Dependencies:** none

---

### BE-02 · Auth & roles
**Context:** Every endpoint needs to know who's calling and what they're allowed to do.
**Requirements:**
- `Microsoft.Identity.Web` JWT validation against Entra ID
- Map Entra security-group Object IDs → roles: `Reporter`, `Responder`, `TeamLead`, `Admin`
- Policies: `CanReportIncident`, `CanTransitionIncident`, `CanAssign`, `CanComment`
- `CurrentUserService`: exposes user id, name, email, roles from the validated JWT

> **Naming as built** (authoritative: [`docs/specs/entra-id-authorization.md`](specs/entra-id-authorization.md) §3, §6, §7):
> `CanAssign` → `CanAssignIncident`, `CanComment` → `CanCommentInternally`; `CurrentUserService` → `ICurrentUser`
> (`Application/Abstractions`) implemented by `CurrentUser` (`Api/Auth`). The suffix-less name is the repo convention for
> read-only views of ambient context. Extra policies: `CanViewIncident`, `CanManageApplications`, `CanViewDashboard`.

**Acceptance Criteria:**
- [ ] Request with no/invalid token → `401`
- [ ] Request with valid token but missing group claim → `403` on policy-protected endpoints
- [ ] `CurrentUserService.Roles` matches the groups in the token for all 4 roles (integration test per role)
**Dependencies:** BE-01

---

### BE-03 · Application catalog
**Context:** Incidents belong to applications owned by teams — this is reference data everything else hangs off.
**Requirements:**
- Entities: `Application` (Id, Name, Code, OwningTeamId, EscalationUserId, Environments[], IsActive), `Team` (Id, Name, Email, TeamsChannelUrl), `TeamMember` (TeamId, UserId, Role), `User` (Id, ExternalId, DisplayName, Email, NotificationPrefs JSON)
- EF Core configuration class per entity (no data annotations on entities)
- Initial migration
- Endpoints: `GET/POST /api/v1/applications`, `GET/PUT /api/v1/applications/{id}`, `GET/POST /api/v1/teams`, `PUT /api/v1/teams/{id}`

**Acceptance Criteria:**
- [ ] Migration applies cleanly against an empty database
- [ ] `POST /applications` requires `Admin` role → `403` otherwise
- [ ] Round-trip create → get returns identical data
**Dependencies:** BE-01, BE-02

---

### BE-04 · Incident domain model
**Context:** The core aggregate. Must enforce the workflow itself — no caller can bypass it.
**Requirements:**
- `Incident` aggregate with full schema (per API contract) + `IncidentHistory` (append-only)
- Workflow enforced only through `Incident.TransitionTo(newStatus, actor, note)`:

  | From | Allowed to | Who |
  |---|---|---|
  | New | Triaged, Closed (duplicate/invalid) | Responder |
  | Triaged | InProgress, OnHold | Responder |
  | InProgress | Resolved, OnHold | Assignee, Responder |
  | OnHold | InProgress | Assignee, Responder |
  | Resolved | Closed, Reopened | Reporter, Responder |
  | Reopened | InProgress | Responder |

- Domain events: `IncidentReported`, `IncidentTransitioned`, `IncidentAssigned`, `IncidentUpdated`
- `Incident.Number` from a SQL `SEQUENCE` (format `INC-{n}`); `Id` (Guid) is the internal key
- Indexes: `(Status, Severity) INCLUDE (ApplicationId, AssigneeId)`, `(ApplicationId, CreatedAt)`, full-text on `Title`+`Description`; partial unique index on `Fingerprint WHERE Status <> 'Closed'`

**Acceptance Criteria:**
- [ ] Every row in the transition table has a passing unit test; every transition NOT in the table has a test asserting it throws
- [ ] Attempting to set `Status` from outside `TransitionTo` is impossible (no public setter)
- [ ] `IncidentHistory` gets a new row on every transition, with old value, new value, actor, timestamp
- [ ] Two incidents with the same `Fingerprint` cannot both be open at once (unique index violation surfaces as a domain conflict, not a raw SQL exception)
**Dependencies:** BE-01, BE-03

---

### BE-05 · Report incident
**Context:** First user-facing write path.
**Requirements:**
- `ReportIncidentCommand` + handler + FluentValidation validator
- Single transaction: incident row + `IncidentHistory` row + `OutboxMessage` row
- `AckDueAt`/`ResolveDueAt` set from the (default, until BE-12) SLA policy at creation time
- `POST /api/v1/incidents` → `201` + `Location` header; honors `Idempotency-Key` header (same key + same body → same result, no duplicate row)

**Acceptance Criteria:**
- [ ] Invalid payload → `400`/`422` with field-level `ProblemDetails.errors`
- [ ] Two requests with the same `Idempotency-Key` within its TTL produce exactly one incident
- [ ] `AckDueAt`/`ResolveDueAt` are non-null and correct for the incident's severity immediately after creation
- [ ] Outbox row exists in the same DB transaction as the incident (verified by rollback test)
**Dependencies:** BE-02, BE-04

---

### BE-06 · Duplicate hints
**Context:** Reduce duplicate reports by surfacing likely matches while the user types.
**Requirements:**
- `GET /api/v1/incidents/similar?applicationId=&title=` — full-text search, up to 5 open incidents, same application

**Acceptance Criteria:**
- [ ] Returns only incidents with `Status != Closed` for the given application
- [ ] Response time < 300ms p95 against a seeded 50k-row table
**Dependencies:** BE-04

---

### BE-07 · Incident list, detail, transition, assignment
**Context:** Core read/write surface for day-to-day triage work.
**Requirements:**
- `GET /api/v1/incidents` — cursor paging; filters: status, severity, applicationId, environment, assigneeId (`me` shorthand), teamId, reporterId, slaBreached, text `q`, date range; sortable
- `GET /api/v1/incidents/{number}` — full detail; includes computed `allowedTransitions`, `canEdit`, `canAssign` for the caller; returns `ETag`
- `PATCH /api/v1/incidents/{id}` — edit title/description/severity(+reason)/priority/application; requires `If-Match`; `409` on stale `RowVersion`
- `POST /api/v1/incidents/{id}/transitions` — validates state machine + role; appends history; requires `If-Match`
- `POST /api/v1/incidents/{id}/assignment` — assign to user or team; appends history; requires `If-Match`

**Acceptance Criteria:**
- [ ] List queries use `AsNoTracking()` + `Select` projection — no full aggregate loads (verified by query plan / profiler check)
- [ ] Forbidden transition for the caller's role → `403`; stale `If-Match` → `409`
- [ ] `allowedTransitions` in the detail response exactly matches the domain's role/status table for the caller's role
**Dependencies:** BE-04, BE-05

---

### BE-08 · Comments & attachments
**Context:** Collaboration and evidence on an incident.
**Requirements:**
- `Comment` entity (Id, IncidentId, AuthorId, sanitized `Body`, `IsInternal`, timestamps)
- `Attachment` entity with `ScanStatus` (Pending/Clean/Infected)
- `POST /api/v1/attachments` — multipart upload to blob storage, ≤25 MB, allowed types only; sets `ScanStatus = Pending`
- Virus-scan hook (stub in v1)
- `GET /api/v1/attachments/{id}/download` — `302` to a 5-minute SAS link; `409` if not `Clean`
- `GET/POST /api/v1/incidents/{id}/comments` — paged list, create; `Comment` creation appends to history
- `GET /api/v1/incidents/{id}/history` — paged, newest first

**Acceptance Criteria:**
- [ ] A `Reporter` calling the comments list never receives a comment with `IsInternal = true` (API-level test, not just UI hiding)
- [ ] Comment body is sanitized — a `<script>` payload is stripped before storage
- [ ] Download of a `Pending`/`Infected` attachment → `409`; `Clean` → `302` with a link that expires after 5 minutes
- [ ] Oversized or disallowed-type upload → `413`/`422`
**Dependencies:** BE-04, BE-07

---

### BE-09 · SignalR real-time
**Context:** Live updates to the dashboard and watched incidents.
**Requirements:**
- `Microsoft.AspNetCore.SignalR` + Redis backplane
- `IncidentHub : Hub<IIncidentClient>` (strongly typed, no string method names)
- `IIncidentClient`: `IncidentCreated`, `IncidentUpdated`, `CommentAdded`, `AppStatusChanged`, `DashboardChanged`, `NotificationReceived`
- Group management: `JoinDashboard`, `WatchIncident(id)`, `UnwatchIncident(id)` — each checked against team access
- Broadcasts fire from a post-commit domain-event dispatcher, never mid-transaction
- `DashboardChanged` throttled to 1/second/group

**Acceptance Criteria:**
- [ ] A test client joined to `WatchIncident(id)` receives `IncidentUpdated` within 1s of a committed change, and nothing before commit
- [ ] A user without access to an incident's application is rejected from `WatchIncident`
- [ ] Two rapid updates within 1s to the same dashboard group produce at most one `DashboardChanged` event
**Dependencies:** BE-04, BE-07

---

### BE-10 · Outbox dispatcher (email)
**Context:** Reliable, async notification delivery without blocking request handlers.
**Requirements:**
- `OutboxMessage` table (Id, Type, Payload JSON, OccurredAt, ProcessedAt, Attempts, Error)
- `OutboxDispatcher` BackgroundService: poll every 2s, `UPDLOCK READPAST`, batch of 50
- `INotificationSender` + `EmailSender` (SMTP/SendGrid)
- Triggers: incident created → owning team; assigned → assignee; resolved → reporter
- Polly: timeout + jittered retry + circuit breaker; failures increment `Attempts` with exponential backoff

**Acceptance Criteria:**
- [ ] No handler calls `EmailSender` directly — only the dispatcher does
- [ ] A send failure doesn't lose the message — `Attempts` increments and it's retried later, not deleted
- [ ] Two dispatcher instances running concurrently never double-send the same message (claimed-row test)
**Dependencies:** BE-05, BE-07

---

### BE-11 · Basic dashboard
**Context:** The at-a-glance view of system health, derived — never user-set.
**Requirements:**
- `AppStatusCalculator`: open Critical in Production → `Outage`; open High → `Degraded`; else `Operational`; cached in Redis, invalidated on every relevant incident change
- `GET /api/v1/dashboard/summary?range=7d` — counts by status/severity/application, SLA breaches, MTTA, MTTR; Redis-first with SQL fallback, 5s TTL
- `GET /api/v1/dashboard/app-status` — current status per application
- `GET /api/v1/me` — current user DTO
- OpenAPI spec generated to `contracts/swagger.json`; CI fails on unreviewed diff

**Acceptance Criteria:**
- [ ] Status is never settable via any API — only computed
- [ ] Redis cache miss falls back to SQL and repopulates the cache
- [ ] `swagger.json` regenerates deterministically (no spurious diffs between identical runs)
**Dependencies:** BE-04, BE-07, BE-09

---

## PHASE 2

### BE-12 · SLA policies & escalation
**Context:** Replace the default SLA with per-severity configurable policies, and act on breaches automatically.
**Requirements:**
- `SlaPolicy` entity: Severity, AckMinutes, ResolveMinutes, EscalateAtPercent
- `GET/PUT /api/v1/sla-policies` (Admin only)
- `SlaMonitor` BackgroundService: every minute; warns assignee at `EscalateAtPercent`; on breach sets `IsSlaBreached = true` and escalates to the application's escalation contact
- Redis distributed lock — exactly one worker instance runs `SlaMonitor` at a time
- Outbox types: `SlaWarning`, `SlaBreached`

**Acceptance Criteria:**
- [ ] Changing a policy's minutes doesn't retroactively change already-set `AckDueAt`/`ResolveDueAt` on existing incidents (only new/severity-changed ones)
- [ ] With two worker replicas running, only one executes `SlaMonitor` per tick (lock contention test)
- [ ] An incident crossing `EscalateAtPercent` produces exactly one warning outbox message, not one per tick
**Dependencies:** BE-05, BE-10

---

### BE-13 · Teams / Slack notifications
**Context:** Notification channels beyond email.
**Requirements:**
- `TeamsNotificationSender` and/or `SlackNotificationSender` implementing `INotificationSender`
- Route by application's `TeamsChannelUrl` / Slack webhook
- Per-user channel preference from `User.NotificationPrefs`

**Acceptance Criteria:**
- [ ] A user with `NotificationPrefs = Teams` receives no email for the same event
- [ ] Missing/invalid webhook URL fails gracefully (logged, retried via Polly) without crashing the dispatcher
**Dependencies:** BE-10

---

### BE-14 · In-app notifications
**Context:** Notification bell needs a backing store and real-time delivery.
**Requirements:**
- `Notification` table (per schema)
- Outbox handler writes a `Notification` row and sends `NotificationReceived` to `user:{userId}` via SignalR
- `GET /api/v1/me/notifications?unreadOnly=true` (paged)
- `PATCH /api/v1/me/notifications/{id}`, `POST /api/v1/me/notifications/read-all`

**Acceptance Criteria:**
- [ ] A notification appears in the paged list even if the user was offline when it was sent (SignalR is additive, not the source of truth)
- [ ] `read-all` marks every unread notification for that user, no others
**Dependencies:** BE-09, BE-10

---

### BE-15 · MTTA/MTTR nightly rollup
**Context:** Trend charts need pre-aggregated data, not computed-on-read.
**Requirements:**
- `DailyStatsJob` BackgroundService at midnight → writes `IncidentDailyStats` (date, applicationId, reported, resolved, avgMttaMinutes, avgMttrMinutes)
- Dashboard summary reads this table for trend data

**Acceptance Criteria:**
- [ ] Re-running the job for the same date is idempotent (upsert, not insert-duplicate)
- [ ] Rollup numbers match a manual recomputation from raw incident timestamps for a seeded day
**Dependencies:** BE-11

---

### BE-16 · CSV export
**Context:** Offline reporting for stakeholders who don't use the dashboard.
**Requirements:**
- `GET /api/v1/reports/incidents.csv?from=&to=&applicationId=` — streamed, reads from replica
- Columns: Number, Title, Application, Environment, Severity, Priority, Status, Reporter, Assignee, CreatedAt, AcknowledgedAt, ResolvedAt, ClosedAt, MTTA(min), MTTR(min)

**Acceptance Criteria:**
- [ ] Response is streamed (no full in-memory buffering for large ranges) — verified with a large seeded dataset
- [ ] Column order and headers match the spec exactly (contract test)
**Dependencies:** BE-07

---

### BE-17 · Alert webhook (external integration)
**Context:** Allow external monitoring tools to open incidents automatically.
**Requirements:**
- `POST /api/v1/integrations/alerts` — HMAC-SHA256 signature validation
- Creates a new incident, or appends to an existing open one matching `externalId`
- `Source = Webhook`

**Acceptance Criteria:**
- [ ] Invalid/missing signature → `401`, no incident created
- [ ] Second call with the same `externalId` while the first incident is still open appends rather than duplicating
**Dependencies:** BE-05

---

## PHASE 3 (future — not yet spec'd in detail)
- [ ] Public status page API
- [ ] Post-incident review templates
- [ ] Jira / Azure DevOps linking
- [ ] On-call rotation scheduling
- [ ] Advanced duplicate detection (beyond full-text)

---

## CROSS-CUTTING

### Testing
- [ ] Domain: every allowed AND forbidden transition per role, SLA calculations, `AppStatusCalculator` rules — pure unit tests, no mocks needed
- [ ] Application: handlers with NSubstitute fakes; validators via FluentValidation `TestValidate`
- [ ] Integration: `WebApplicationFactory` + Testcontainers (SQL Server, Redis) — auth policies, 409 on stale RowVersion, outbox row in same transaction, SignalR broadcast received by a test client
- [ ] OpenAPI snapshot in CI — fail the build on an unreviewed `swagger.json` diff
- [ ] Load test (k6): 200 concurrent dashboard sessions, burst of 50 reports/min
- [ ] Naming: `Method_Scenario_ExpectedResult`; FluentAssertions; don't mock `DbContext` — use the container
- [ ] New feature = tests land in the same PR, no exceptions

### CI/CD
- [ ] GitHub Actions: build → unit tests → integration tests (Testcontainers) → OpenAPI diff → deploy staging → Playwright smoke → manual approval → production
- [ ] EF Core migrations applied via migration bundle in the pipeline — never `Database.Migrate()` on startup

### Security
- [ ] CORS restricted to the SPA origin only
- [ ] Rate limiting on `POST /incidents` and `POST /attachments`
- [ ] No secrets in `appsettings.*` — user-secrets locally, Key Vault in Azure
- [ ] All stored and returned timestamps are UTC (`DateTimeOffset`, via injected `TimeProvider`, never `DateTime.Now`/`UtcNow` directly)
