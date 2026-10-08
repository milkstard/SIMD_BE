# System Design: Service Incident & Monitoring Dashboard

Oct 6, 2026 · @Jmark

## Overview

The system is a web application where users report incidents in the applications they use or support, track each incident from report to resolution, and monitor the overall health of every application on a live dashboard. It is a React + TypeScript SPA on an ASP.NET Core (.NET 8) Web API, with SignalR pushing incident changes to every open browser in under 2 seconds.

**Goals**

- **Report**: anyone signed in can raise an incident in under a minute: application, environment, severity, description, steps to reproduce, screenshots or log files.
- **Track**: responders triage, assign, comment and move incidents through a clear workflow, with a full history of who did what and when.
- **Monitor**: a dashboard shows open incidents per application and severity, each application's current status (Operational, Degraded, Outage), SLA breaches and trends such as MTTA and MTTR.
- **Notify**: the right people hear about new, escalated and changed incidents by email, Teams/Slack and in-app notifications.
- **Learn**: searchable history and reports for post-incident reviews.

**Non-goals (v1)**

- Collecting raw metrics, logs or traces; that stays in existing tools (App Insights, Grafana, Seq). In phase 2 those tools can open incidents through a webhook.
- A general-purpose ticketing system for feature requests or service requests.
- On-call rotation scheduling; v1 uses an owning team and an escalation contact per application.

## Requirements

The system targets about 2,000 users, 100 applications, 200 concurrent dashboard sessions and up to 500 new incidents a day, with 99.9% availability.

**Functional**

| ID | Requirement | Priority |
| --- | --- | --- |
| F1 | Report an incident: application, environment (Prod / UAT / Dev), severity, title, description, steps to reproduce, attachments | Must |
| F2 | Duplicate hint while typing: show open incidents for the same application with similar titles | Should |
| F3 | Workflow: New → Triaged → In Progress → Resolved → Closed, plus On Hold and Reopened; transitions limited by role | Must |
| F4 | Assign to a person or team; change severity and priority with a reason | Must |
| F5 | Comments (internal or reporter-visible), @mentions, attachments, and an automatic activity timeline | Must |
| F6 | Search and filter by application, status, severity, assignee, date, text; saved views ("My incidents", "Unassigned P1") | Must |
| F7 | Monitoring dashboard: open incidents by application and severity, application status board, SLA breaches, MTTA / MTTR trends | Must |
| F8 | Real-time updates on lists, detail pages and the dashboard without refresh | Must |
| F9 | Notifications on assignment, status change, mention, SLA warning and breach; per-user preferences | Must |
| F10 | SLA policies per severity (time to acknowledge, time to resolve) with escalation | Should |
| F11 | Application catalog: owning team, escalation contact, environments | Must |
| F12 | Reports and CSV export for post-incident reviews | Should |
| F13 | Webhook so monitoring tools can open incidents automatically (phase 2) | Could |

**Non-functional**

| Area | Target |
| --- | --- |
| Real-time | A change is visible to other users within 2 s |
| API response | p95 under 300 ms for reads, under 500 ms for writes |
| Availability | 99.9% for API and SPA |
| Attachments | Up to 25 MB per file, virus-scanned, image and log types only |
| Retention | Incidents and history kept indefinitely; attachments 2 years |
| Security | SSO (Entra ID / OIDC), role-based access, audit log of every change |
| Accessibility | WCAG 2.1 AA; status and severity never shown by colour alone |
|  |  |

## High-level architecture

Two ASP.NET Core processes share one codebase: the Web API handles every user action and pushes changes over SignalR, and the Background Worker sends notifications and enforces SLAs. A user's request never waits on email or Teams, because the API only writes an outbox row and the worker sends it.

&#91;embedded content: system architecture · SPA, API, worker, 3 stores\]

A report flows top to bottom: the SPA calls the API, the API saves the incident and an outbox message in one transaction and broadcasts to open dashboards, and the worker picks up the outbox to notify the owning team.

## Backend design (ASP.NET Core)

The backend is a modular monolith using Clean Architecture and CQRS with MediatR. The incident workflow lives in the domain model, so the same rules apply whether a change comes from the UI, the webhook or the worker.

**Solution layout**

```
src/
  IncidentHub.Domain/          // Incident aggregate, workflow state machine, domain events
  IncidentHub.Application/     // Commands/queries (MediatR), FluentValidation, DTOs, interfaces
  IncidentHub.Infrastructure/  // EF Core, Redis, Blob Storage, email/Teams senders, outbox
  IncidentHub.Api/             // Controllers, SignalR IncidentHub, auth policies, ProblemDetails
  IncidentHub.Worker/          // BackgroundService: outbox dispatcher, SLA timer, digests
tests/
  Domain.UnitTests, Application.UnitTests, Api.IntegrationTests (Testcontainers)
```

**Modules**

| Module | Responsibility | Key types |
| --- | --- | --- |
| Incidents | Report, update, assign, transition, link duplicates | `Incident`, `ReportIncidentCommand`, `TransitionIncidentCommand` |
| Comments & Attachments | Comments with visibility, @mentions, file upload to Blob Storage | `Comment`, `Attachment`, `IFileStore` |
| Application Catalog | Applications, environments, owning team, escalation contact | `Application`, `Team` |
| SLA | Policies per severity; due times computed on create and on severity change | `SlaPolicy`, `SlaClock` |
| Dashboard & Reports | Aggregated counts, app status, MTTA / MTTR, CSV export | `DashboardQuery`, `AppStatusCalculator` |
| Notifications | Turns domain events into email, Teams/Slack and in-app messages based on user preferences | `NotificationRule`, `INotificationSender` |
| Realtime | Broadcasts changes to SignalR groups | `IncidentHub`, `IIncidentClient` |

**Report-to-resolution flow**

1. A user submits the report form; the SPA uploads attachments first (`POST /attachments`, returns ids), then posts the incident.
2. `ReportIncidentCommand` is validated (FluentValidation), the application's owning team is set as default assignee group, and SLA due times are calculated from the severity.
3. The `Incident` aggregate raises `IncidentReported`. EF Core saves the incident, a `History` row and an `OutboxMessage` in **one transaction**.
4. After commit, the API broadcasts `IncidentCreated` to the `dashboard` and `app:{id}` SignalR groups; open dashboards update their counts.
5. The worker polls the outbox every 2 s, sends notifications to the owning team, and marks the message processed.
6. A responder triages and assigns → `POST /incidents/{id}/transitions` → the state machine checks the move and role → history row, broadcast, notifications.
7. Every minute the worker checks SLA clocks: at 80% it warns the assignee; on breach it escalates to the application's escalation contact and flags the incident on the dashboard.

**Workflow rules (domain)**

| From | Allowed to | Who |
| --- | --- | --- |
| New | Triaged, Closed (duplicate / invalid) | Responder |
| Triaged | In Progress, On Hold | Responder |
| In Progress | Resolved, On Hold | Assignee, Responder |
| On Hold | In Progress | Assignee, Responder |
| Resolved | Closed, Reopened | Reporter, Responder |
| Reopened | In Progress | Responder |

**Key implementation choices**

- **Application status** is derived, not typed: any open Critical incident in Prod = Outage, any open High = Degraded, otherwise Operational. Recomputed on each incident change and cached in Redis.
- **Concurrency**: `RowVersion` on `Incident`; a stale update returns `409` so two responders cannot overwrite each other.
- **Idempotency**: create endpoints accept an `Idempotency-Key` header so a double-click never makes two incidents.
- **Resilience**: Polly retries and circuit breakers on email/Teams senders; failed notifications stay in the outbox for retry.

## Data model and storage

Data is split by access pattern: SQL Server holds all incident data, Redis holds cached dashboard figures and the SignalR backplane, and Blob Storage holds attachments. No time-series store is needed, because dashboard trends are computed from incident timestamps.

| Store | Holds | Why |
| --- | --- | --- |
| SQL Server (EF Core 8) | Incidents, comments, history, applications, teams, SLA policies, notifications, audit log, outbox | Transactions for the workflow, full-text search on title and description, familiar to .NET teams |
| Redis | Dashboard counts, application status, SignalR backplane, distributed locks for the worker | Sub-millisecond dashboard reads; lets the API scale out |
| Blob Storage | Screenshots and log files | Cheap large files; downloads through short-lived SAS links |

**Core tables**

```
Application     (Id, Name, Code, OwningTeamId, EscalationUserId, IsActive)
Team            (Id, Name, Email, TeamsChannelUrl)
TeamMember      (TeamId, UserId, Role)
User            (Id, ExternalId, DisplayName, Email, NotificationPrefs JSON)
Incident        (Id, Number, Title, Description, StepsToReproduce, ApplicationId, Environment,
                 Severity, Priority, Status, ReporterId, AssigneeId, AssignedTeamId,
                 Source /* Manual | Webhook */, DuplicateOfId,
                 CreatedAt, AcknowledgedAt, ResolvedAt, ClosedAt,
                 AckDueAt, ResolveDueAt, IsSlaBreached, RowVersion)
Comment         (Id, IncidentId, AuthorId, Body, IsInternal, CreatedAt, EditedAt)
Attachment      (Id, IncidentId, CommentId?, FileName, ContentType, SizeBytes, BlobPath, ScanStatus, UploadedBy, UploadedAt)
IncidentHistory (Id, IncidentId, ActorId, Field, OldValue, NewValue, At)   -- append-only timeline
SlaPolicy       (Id, Severity, AckMinutes, ResolveMinutes, EscalateAtPercent)
Notification    (Id, UserId, IncidentId, Type, Message, IsRead, CreatedAt)   -- in-app bell
AuditLog        (Id, ActorId, Action, EntityType, EntityId, Before JSON, After JSON, At)
OutboxMessage   (Id, Type, Payload JSON, OccurredAt, ProcessedAt, Attempts, Error)
```

**Indexes and notes**

- `IX_Incident_Status_Severity (Status, Severity) INCLUDE (ApplicationId, AssigneeId)` for list filters and dashboard counts.
- `IX_Incident_Application_Created (ApplicationId, CreatedAt)` for per-application trends; full-text index on `Title`, `Description`.
- `Number` is a human-friendly sequence (INC-1042) from a SQL `SEQUENCE`; URLs use it, the API keys on `Id`.
- MTTA = `AcknowledgedAt - CreatedAt`, MTTR = `ResolvedAt - CreatedAt`; a nightly job writes daily rollups to `IncidentDailyStats` for fast reports.

## API design

REST (versioned under `/api/v1`, documented with OpenAPI) handles every user action; SignalR pushes changes back. TypeScript types are generated from the OpenAPI spec (`openapi-typescript` or NSwag) so frontend and backend contracts cannot drift.

**REST endpoints**

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/v1/incidents` | Report an incident |
| GET | `/api/v1/incidents?status=&severity=&appId=&assignee=me&q=&cursor=` | Search and filter, cursor-paged |
| GET | `/api/v1/incidents/{number}` | Detail with comments, attachments, history |
| PATCH | `/api/v1/incidents/{id}` | Edit fields (title, severity, priority, application); `If-Match` RowVersion |
| POST | `/api/v1/incidents/{id}/transitions` | Change status: `{ "to": "Resolved", "note": "..." }` |
| POST | `/api/v1/incidents/{id}/assignment` | Assign to a user or team |
| GET | `/api/v1/incidents/similar?appId=&title=` | Duplicate hints while reporting |
| POST | `/api/v1/incidents/{id}/comments` | Add a comment (internal or public) |
| POST | `/api/v1/attachments` | Upload a file (multipart), returns attachment id |
| GET | `/api/v1/attachments/{id}/download` | Redirect to a short-lived SAS link |
| GET | `/api/v1/dashboard/summary?range=7d` | Counts by status, severity, application; SLA breaches; MTTA / MTTR |
| GET | `/api/v1/dashboard/app-status` | Current status per application |
| GET | `/api/v1/reports/incidents.csv?from=&to=` | CSV export |
| GET / POST / PUT | `/api/v1/applications`, `/api/v1/teams`, `/api/v1/sla-policies` | Admin configuration |
| GET / PATCH | `/api/v1/me/notifications` | In-app notifications, mark read |
| POST | `/api/v1/integrations/alerts` | Phase 2 webhook for monitoring tools (HMAC-signed) |

Errors use RFC 7807 `ProblemDetails` with a `traceId`: `400`/`422` for validation, `403` for a transition the role may not make, `409` for an invalid or stale change.

**Example: report an incident**

```http
POST /api/v1/incidents
Idempotency-Key: 6b1e...

{
  "applicationId": "b3f1...",
  "environment": "Production",
  "severity": "High",
  "title": "Checkout fails with 500 after payment",
  "description": "Since 09:40 users see an error page after paying...",
  "stepsToReproduce": "1. Add item 2. Pay with card 3. Error page",
  "attachmentIds": ["a91c..."]
}

201 Created  Location: /api/v1/incidents/INC-1042
{ "id": "8f2c...", "number": "INC-1042", "status": "New", "ackDueAt": "2026-10-06T10:15:00Z", ... }
```

**SignalR hub: `/hubs/incidents`**

| Direction | Message | Payload |
| --- | --- | --- |
| Client → Server | `JoinDashboard()` | Joins group `dashboard` |
| Client → Server | `WatchIncident(id)` / `UnwatchIncident(id)` | Joins group `incident:{id}` |
| Server → Client | `IncidentCreated` / `IncidentUpdated` | `IncidentSummaryDto` |
| Server → Client | `CommentAdded` | `{ incidentId, comment }` (internal comments only to responders) |
| Server → Client | `AppStatusChanged` | `{ applicationId, status }` |
| Server → Client | `DashboardChanged` | Updated summary counts (throttled to 1 per second) |
| Server → Client | `NotificationReceived` | To the user's own connection, for the bell icon |

The hub is strongly typed (`Hub<IIncidentClient>`), and the Redis backplane lets any API instance broadcast to every client.

## Frontend design (React + TypeScript)

The SPA is built with Vite, React 18 and strict TypeScript. Server data lives in TanStack Query; SignalR events patch that cache directly, so a pushed change and a refetch land in the same place and no component manages sockets itself.

**Stack**

| Concern | Choice |
| --- | --- |
| Build | Vite, ESLint, Prettier, `strict: true` |
| Routing | React Router 6, lazy-loaded routes |
| Server state | TanStack Query (cache, retries, optimistic updates) |
| UI state | Zustand (filters, saved views, theme) |
| Real-time | `@microsoft/signalr` with automatic reconnect |
| UI kit | MUI (DataGrid for lists) or shadcn/ui + Tailwind; Recharts for trends |
| Forms | React Hook Form + Zod |
| Auth | MSAL React (Entra ID); token added by a fetch interceptor and SignalR `accessTokenFactory` |
| Testing | Vitest, React Testing Library, MSW, Playwright |

**Folder structure**

```
src/
  app/            // providers (QueryClient, Auth, Theme, SignalR), router, layout
  api/            // generated types + typed client, query keys
  realtime/       // SignalRProvider, cache-patching handlers
  features/
    report/       // ReportIncidentForm, AttachmentDropzone, DuplicateHints
    incidents/    // IncidentList, IncidentDetail, Timeline, CommentBox, StatusActions
    dashboard/    // KpiStrip, AppStatusBoard, SeverityChart, SlaBreachList, TrendChart
    admin/        // Applications, Teams, SlaPolicies
    notifications/// Bell, NotificationList
  shared/         // StatusBadge, SeverityChip, UserAvatar, TimeAgo, ErrorBoundary
```

**Screens**

- **Report incident**: one-page form with application picker, environment, severity (with plain-language help for each level), rich-text description, drag-and-drop attachments, and duplicate hints as the title is typed.
- **Incident list**: filterable grid with saved views (My incidents, My team, Unassigned, SLA at risk) and live row updates.
- **Incident detail**: header with status actions allowed for the user's role, assignee, severity and SLA countdown; tabs for comments, history and attachments.
- **Monitoring dashboard**: KPI strip (open, unassigned, SLA breached, MTTR this week), application status board, open incidents by severity and application, 30-day trend of reported vs resolved.
- **Admin**: applications, teams, SLA policies.

**Real-time integration**

```ts
// realtime/handlers.ts
export function registerHandlers(conn: HubConnection, qc: QueryClient) {
  conn.on('IncidentUpdated', (dto: IncidentSummaryDto) => {
    qc.setQueriesData<Paged<IncidentSummaryDto>>({ queryKey: incidentKeys.lists() },
      page => page && { ...page, items: upsertById(page.items, dto) });
    qc.setQueryData<IncidentDetailDto>(incidentKeys.detail(dto.number),
      prev => prev && { ...prev, ...dto });
  });

  conn.on('DashboardChanged', (s: DashboardSummaryDto) =>
    qc.setQueryData(dashboardKeys.summary(), s));

  conn.onreconnected(() => qc.invalidateQueries()); // resync anything missed while offline
}
```

**Typing the domain**

```ts
export type Severity = 'Critical' | 'High' | 'Medium' | 'Low';
export type IncidentStatus =
  'New' | 'Triaged' | 'InProgress' | 'OnHold' | 'Resolved' | 'Reopened' | 'Closed';
export type AppStatus = 'Operational' | 'Degraded' | 'Outage';

// UI mirror of the server workflow; the server stays the authority
export const nextStatuses: Record<IncidentStatus, IncidentStatus[]> = {
  New: ['Triaged', 'Closed'],
  Triaged: ['InProgress', 'OnHold'],
  InProgress: ['Resolved', 'OnHold'],
  OnHold: ['InProgress'],
  Resolved: ['Closed', 'Reopened'],
  Reopened: ['InProgress'],
  Closed: [],
};
```

**UX and performance rules**

- Optimistic status and assignment changes, rolled back on `409` with a toast saying who changed it.
- The report form saves a draft locally per user so a refresh never loses a long description.
- A connection banner shows Live / Reconnecting / Offline.
- Long lists use server paging and virtualised rows; charts read pre-aggregated dashboard data.
- Severity and status use icon + text + colour; new Critical incidents announce through an `aria-live` region.

## Security, scalability and reliability

**Security**

| Role | Can |
| --- | --- |
| Reporter | Report incidents, view and comment on incidents for their applications, confirm a resolution |
| Responder | Everything a Reporter can, plus triage, assign, change status and severity, add internal comments |
| Team Lead | Responder rights plus team dashboards, reassigning across the team, reports |
| Admin | Manage applications, teams, SLA policies and users |

- Users sign in through Entra ID / OIDC; the API validates JWTs and maps groups to roles.
- Policy-based authorization in ASP.NET (`[Authorize(Policy = "CanTransitionIncident")]`) plus resource checks (is the user on the application's team?). SignalR group joins go through the same checks, and internal comments are never sent to Reporters.
- Attachments: type and size checked on upload, virus-scanned before download is allowed, served only through SAS links that expire in 5 minutes.
- Rich-text descriptions are sanitised on the server (HtmlSanitizer) to block stored XSS.
- Secrets in Key Vault, TLS everywhere, CORS limited to the SPA origin, CSP headers, rate limiting on create endpoints.
- Every change is written to `IncidentHistory` and `AuditLog`.

**Scalability**

| Component | Scales by | Guard |
| --- | --- | --- |
| Web API | Horizontal, stateless; Redis backplane for SignalR | Output caching for dashboard endpoints (5 s) |
| Background Worker | 1 to 2 instances; outbox rows claimed with `UPDLOCK, READPAST` | Batch of 50 messages per poll |
| SQL Server | Indexes above, read replica for reports and CSV export | Dashboard reads Redis first |
| Blob Storage | Managed | Direct upload size limit 25 MB |

**Reliability**

- Outbox + idempotent senders give at-least-once notifications without blocking user actions.
- If Redis is down, the dashboard falls back to SQL and the SPA polls every 30 s until SignalR reconnects.
- If email or Teams is down, messages stay in the outbox with exponential back-off and an alert after 5 failures.

**Observability**

- OpenTelemetry traces and metrics from API and worker (Serilog → OTLP / App Insights), `traceId` returned in every `ProblemDetails`.
- Key internal metrics: outbox lag, notification failures, SignalR connections, p95 API latency.

## Deployment, testing and roadmap

**Deployment**

- API and worker as containers on Azure App Service or Container Apps; the SPA on Azure Static Web Apps behind a CDN.
- Managed services: Azure SQL, Azure Cache for Redis, Blob Storage with Defender malware scanning.
- GitHub Actions or Azure DevOps: build → unit tests → integration tests (Testcontainers) → OpenAPI diff check → deploy to staging → Playwright smoke → approval → production.
- EF Core migrations run as a pipeline step (migration bundle), never on app start; infrastructure in Bicep or Terraform.
- Local dev: `docker compose up` starts SQL Server, Redis and Azurite.

**Testing strategy**

| Layer | Tooling | Focus |
| --- | --- | --- |
| Domain unit | xUnit, FluentAssertions | Workflow transitions per role, SLA due times, app status rules |
| Application unit | xUnit, NSubstitute | Command validation, notification rules |
| Integration | WebApplicationFactory + Testcontainers | Endpoints, EF mappings, outbox, SignalR broadcasts, auth policies |
| Contract | OpenAPI snapshot + generated TS types | Breaking API changes caught in CI |
| Frontend unit | Vitest, React Testing Library, MSW | Report form, status actions, cache patching |
| E2E | Playwright | Report → triage → assign → resolve → close, with a second browser seeing live updates |
| Load | k6 | 200 dashboard sessions, burst of 50 reports a minute |

**Phased roadmap**

1. **Phase 1 (MVP, \~6 weeks)**: report form with attachments, incident list and detail, workflow and assignment, comments, real-time updates, basic dashboard, email notifications.
2. **Phase 2 (\~4 weeks)**: SLA policies and escalation, Teams/Slack, in-app notifications, application status board, MTTA / MTTR trends, CSV export, monitoring-tool webhook.
3. **Phase 3**: public status page, post-incident review templates, Jira / Azure DevOps linking, on-call schedules.

**Key trade-offs**

| Decision | Chosen | Alternative | Reason |
| --- | --- | --- | --- |
| Service shape | Modular monolith, API + worker | Microservices | Small team, simple ops; modules can be split later |
| Real-time | SignalR | Polling / SSE | Native to ASP.NET, groups, reconnect built in |
| Notifications | Outbox table + worker | Message broker (Service Bus) | One database, transactional, enough for 500 incidents a day; add a broker if volume grows |
| Storage | SQL Server only (+ Redis cache) | SQL + time-series DB | Trends come from incident timestamps, not raw metrics |
| Frontend state | TanStack Query + Zustand | Redux Toolkit | Less boilerplate; server cache is the main state |

**Open questions**

- [ ] Is Azure the target cloud, or does this need to run on-premises?
- [ ] Should monitoring tools open incidents automatically in v1, or can that wait for the phase 2 webhook?
- [ ] Which chat tool is primary for notifications: Slack or Teams?
