# API Contract — IncidentHub (Service Incident & Monitoring Dashboard)

Version: **v1** · Base URL: `/api/v1` · Real-time hub: `/hubs/incidents`

This is the agreement between the ASP.NET Core backend and the React + TypeScript frontend.
The backend owns the implementation; the OpenAPI spec (`contracts/swagger.json`) is generated from it and
must match this document. The frontend generates its types from that spec — it never hand-writes DTOs.

**Change rule:** any change to a model, endpoint, enum or event updates this file in the same PR.
Additive changes (new optional field, new endpoint) are fine in v1. Removing or renaming anything is a
breaking change and needs `/api/v2` or a coordinated release of both sides.

---

## 1. Conventions

| Topic | Rule |
|---|---|
| Format | JSON, UTF-8, `camelCase` property names (System.Text.Json default) |
| Auth | `Authorization: Bearer <JWT>` from Entra ID. SignalR uses `?access_token=` via `accessTokenFactory` |
| IDs | `id` = GUID string. Incidents also have `number` (`"INC-1042"`) for URLs and display |
| Dates | ISO 8601 **UTC** strings (`"2026-10-06T09:40:00Z"`), C# `DateTimeOffset`. Frontend converts to local time for display |
| Enums | Serialized as **strings** (`JsonStringEnumConverter`), never numbers |
| Nulls | Optional fields are present with `null`, not omitted |
| Paging | Cursor-based: request `cursor`, `limit` (default 25, max 100); response `Paged<T>` |
| Concurrency | Incident responses include `rowVersion` (base64) and an `ETag` header. Writes send `If-Match: "<rowVersion>"` |
| Idempotency | `POST` create endpoints accept `Idempotency-Key: <uuid>`; repeated key returns the original result |
| Errors | RFC 7807 `ProblemDetails` (`application/problem+json`) — see section 6 |
| Rich text | `description` and comment `body` are sanitized HTML; the server strips scripts and unsafe attributes |

---

## 2. Enums

```ts
type Severity        = 'Critical' | 'High' | 'Medium' | 'Low';
type Priority        = 'P1' | 'P2' | 'P3' | 'P4';
type Environment     = 'Production' | 'UAT' | 'Development';
type IncidentStatus  = 'New' | 'Triaged' | 'InProgress' | 'OnHold' | 'Resolved' | 'Reopened' | 'Closed';
type AppStatus       = 'Operational' | 'Degraded' | 'Outage';
type IncidentSource  = 'Manual' | 'Webhook';
type Role            = 'Reporter' | 'Responder' | 'TeamLead' | 'Admin';
type CloseReason     = 'Fixed' | 'Duplicate' | 'Invalid' | 'CannotReproduce';
type ScanStatus      = 'Pending' | 'Clean' | 'Infected';
type NotificationType = 'Assigned' | 'StatusChanged' | 'Mentioned' | 'SlaWarning' | 'SlaBreached' | 'Commented';
```

C# side: one `enum` per type in `IncidentHub.Domain` with identical member names.

### Workflow (server is the authority)

| From | Allowed to | Who |
|---|---|---|
| New | Triaged, Closed (with `closeReason` Duplicate/Invalid) | Responder |
| Triaged | InProgress, OnHold | Responder |
| InProgress | Resolved, OnHold | Assignee, Responder |
| OnHold | InProgress | Assignee, Responder |
| Resolved | Closed, Reopened | Reporter, Responder |
| Reopened | InProgress | Responder |

---

## 3. Shared models

```ts
interface Paged<T> {
  items: T[];
  nextCursor: string | null;   // null = last page
  totalCount: number | null;   // only when includeTotal=true
}

interface UserRef {
  id: string;
  displayName: string;
  email: string;
}

interface TeamRef {
  id: string;
  name: string;
}

interface ApplicationRef {
  id: string;
  name: string;
  code: string;               // e.g. "CHECKOUT"
}

interface CurrentUser extends UserRef {
  roles: Role[];
  teamIds: string[];
}
```

---

## 4. Incident models

### IncidentSummary — lists, dashboard feed, SignalR

```ts
interface IncidentSummary {
  id: string;
  number: string;                 // "INC-1042"
  title: string;
  application: ApplicationRef;
  environment: Environment;
  severity: Severity;
  priority: Priority | null;
  status: IncidentStatus;
  assignee: UserRef | null;
  assignedTeam: TeamRef | null;
  reporter: UserRef;
  createdAt: string;
  updatedAt: string;
  ackDueAt: string | null;
  resolveDueAt: string | null;
  isSlaBreached: boolean;
  commentCount: number;
  rowVersion: string;
}
```

### IncidentDetail — detail page

```ts
interface IncidentDetail extends IncidentSummary {
  description: string;            // sanitized HTML
  stepsToReproduce: string | null;
  source: IncidentSource;
  duplicateOf: { id: string; number: string } | null;
  closeReason: CloseReason | null;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
  closedAt: string | null;
  attachments: Attachment[];
  allowedTransitions: IncidentStatus[];   // computed for the CURRENT user; drives action buttons
  canEdit: boolean;
  canAssign: boolean;
}

interface Attachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  scanStatus: ScanStatus;         // download allowed only when 'Clean'
  uploadedBy: UserRef;
  uploadedAt: string;
}

interface Comment {
  id: string;
  incidentId: string;
  author: UserRef;
  body: string;                   // sanitized HTML, may contain @mentions
  isInternal: boolean;            // never returned to Reporters
  attachments: Attachment[];
  createdAt: string;
  editedAt: string | null;
}

interface HistoryEntry {
  id: string;
  actor: UserRef | null;          // null = system (SLA job, webhook)
  field: string;                  // "Status", "Assignee", "Severity", ...
  oldValue: string | null;
  newValue: string | null;
  note: string | null;
  at: string;
}
```

### Requests

```ts
interface ReportIncidentRequest {
  applicationId: string;          // required
  environment: Environment;       // required
  severity: Severity;             // required
  title: string;                  // required, 5–200 chars
  description: string;            // required, max 20,000 chars (HTML)
  stepsToReproduce?: string | null; // max 5,000 chars
  attachmentIds?: string[];       // from POST /attachments, max 10
}

interface UpdateIncidentRequest {   // PATCH — send only fields that change
  title?: string;
  description?: string;
  stepsToReproduce?: string | null;
  severity?: Severity;
  priority?: Priority | null;
  applicationId?: string;
  environment?: Environment;
  reason?: string;                // required when severity changes
}

interface TransitionRequest {
  to: IncidentStatus;
  note?: string | null;           // required for Resolved and Reopened
  closeReason?: CloseReason;      // required when to = 'Closed'
  duplicateOfId?: string;         // required when closeReason = 'Duplicate'
}

interface AssignRequest {
  assigneeId?: string | null;     // user, or null to unassign
  teamId?: string | null;
}

interface AddCommentRequest {
  body: string;                   // required, max 10,000 chars
  isInternal: boolean;            // only Responders may send true
  attachmentIds?: string[];
}
```

C# mirrors (Application layer), e.g.:

```csharp
public sealed record ReportIncidentRequest(
    Guid ApplicationId,
    Environment Environment,
    Severity Severity,
    string Title,
    string Description,
    string? StepsToReproduce,
    IReadOnlyList<Guid>? AttachmentIds);

public sealed record TransitionRequest(
    IncidentStatus To,
    string? Note,
    CloseReason? CloseReason,
    Guid? DuplicateOfId);
```

---

## 5. Endpoints

### Incidents

| Method | Path | Body | Success | Notes |
|---|---|---|---|---|
| POST | `/incidents` | `ReportIncidentRequest` | `201` `IncidentDetail`, `Location` header | `Idempotency-Key` |
| GET | `/incidents` | — | `200` `Paged<IncidentSummary>` | Query below |
| GET | `/incidents/{number}` | — | `200` `IncidentDetail` + `ETag` | |
| PATCH | `/incidents/{id}` | `UpdateIncidentRequest` | `200` `IncidentDetail` | `If-Match` required |
| POST | `/incidents/{id}/transitions` | `TransitionRequest` | `200` `IncidentDetail` | `If-Match` required |
| POST | `/incidents/{id}/assignment` | `AssignRequest` | `200` `IncidentDetail` | `If-Match` required |
| GET | `/incidents/{id}/comments` | — | `200` `Paged<Comment>` | Oldest first |
| POST | `/incidents/{id}/comments` | `AddCommentRequest` | `201` `Comment` | |
| GET | `/incidents/{id}/history` | — | `200` `Paged<HistoryEntry>` | Newest first |
| GET | `/incidents/similar?applicationId=&title=` | — | `200` `IncidentSummary[]` (max 5) | Open incidents only |

`GET /incidents` query parameters (all optional, repeatable where plural):

```
status=New&status=Triaged   severity=Critical   applicationId=<guid>   environment=Production
assigneeId=<guid>|me|unassigned   teamId=<guid>   reporterId=<guid>|me   slaBreached=true
q=<text>   createdFrom=<iso>   createdTo=<iso>
sort=createdAt|-createdAt|severity|-updatedAt   (default -createdAt)
cursor=<string>   limit=25   includeTotal=false
```

### Attachments

| Method | Path | Body | Success | Notes |
|---|---|---|---|---|
| POST | `/attachments` | `multipart/form-data` field `file` | `201` `Attachment` | ≤ 25 MB; images, `.log`, `.txt`, `.json`; starts `scanStatus: Pending` |
| GET | `/attachments/{id}/download` | — | `302` to SAS URL (5 min) | `409` if not `Clean` |

### Dashboard and reports

```ts
interface DashboardSummary {
  range: '24h' | '7d' | '30d';
  generatedAt: string;
  openTotal: number;
  unassigned: number;
  slaBreached: number;
  openBySeverity: Record<Severity, number>;
  openByApplication: { application: ApplicationRef; open: number; critical: number }[];
  mttaMinutes: number | null;     // mean time to acknowledge, within range
  mttrMinutes: number | null;     // mean time to resolve, within range
  trend: { date: string; reported: number; resolved: number }[];  // date = "YYYY-MM-DD"
}

interface AppStatusItem {
  application: ApplicationRef;
  status: AppStatus;
  openIncidents: number;
  worstOpenSeverity: Severity | null;
  since: string | null;           // when the current status started
}
```

| Method | Path | Success |
|---|---|---|
| GET | `/dashboard/summary?range=7d` | `200` `DashboardSummary` |
| GET | `/dashboard/app-status` | `200` `AppStatusItem[]` |
| GET | `/reports/incidents.csv?from=&to=&applicationId=` | `200` `text/csv` |

### Notifications and current user

```ts
interface AppNotification {
  id: string;
  type: NotificationType;
  incident: { id: string; number: string; title: string };
  message: string;
  isRead: boolean;
  createdAt: string;
}
```

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `/me` | — | `200` `CurrentUser` |
| GET | `/me/notifications?unreadOnly=true` | — | `200` `Paged<AppNotification>` |
| PATCH | `/me/notifications/{id}` | `{ "isRead": true }` | `204` |
| POST | `/me/notifications/read-all` | — | `204` |

### Admin (Admin role)

```ts
interface Application {
  id: string; name: string; code: string;
  owningTeam: TeamRef; escalationContact: UserRef | null;
  environments: Environment[]; isActive: boolean;
  rowVersion: string;
}
interface CreateApplicationRequest {
  name: string; code: string;                 // code: 2-20 chars [A-Za-z0-9_-], stored upper-case, unique
  owningTeamId: string; escalationContactId?: string | null;
  environments: Environment[];                // non-empty, distinct
  isActive?: boolean;                         // default true
}
interface UpdateApplicationRequest {          // PUT = full replace, If-Match required
  name: string; code: string; owningTeamId: string; escalationContactId?: string | null;
  environments: Environment[]; isActive: boolean;
}
interface Team {
  id: string; name: string; email: string;
  teamsChannelUrl: string | null;   // absolute https URL
  rowVersion: string;               // base64; also returned as the ETag header
}
interface CreateTeamRequest { name: string; email: string; teamsChannelUrl?: string | null; }   // name <= 200, email <= 320, url <= 500
interface UpdateTeamRequest { name: string; email: string; teamsChannelUrl?: string | null; }   // PUT = full replace
interface SlaPolicy {
  id: string; severity: Severity;
  ackMinutes: number; resolveMinutes: number; escalateAtPercent: number;  // e.g. 80
}
```

| Method | Path |
|---|---|
| GET / POST | `/applications` (see below) |
| GET / PUT | `/applications/{id}` (see below) |
| GET / POST | `/teams` (see below) |
| GET / PUT | `/teams/{id}` (see below) |
| GET / PUT | `/sla-policies`, `/sla-policies/{id}` |

Teams (all verbs are **Admin only**):

| Method | Path | Body | Success | Notes |
|---|---|---|---|---|
| GET | `/teams?cursor=&limit=25&includeTotal=false` | — | `200` `Paged<Team>` | Ordered by `name`, then `id` |
| POST | `/teams` | `CreateTeamRequest` | `201` `Team`, `Location`, `ETag` | `Idempotency-Key` honoured |
| GET | `/teams/{id}` | — | `200` `Team` + `ETag` | `404` if unknown |
| PUT | `/teams/{id}` | `UpdateTeamRequest` | `200` `Team` + `ETag` | `If-Match` required; `428` if missing, `400` if malformed, `409` if stale, `404` if unknown |

Validation failures return `422` with camelCase keys, e.g. `{ "email": ["..."], "teamsChannelUrl": ["..."] }`.
Team names are not unique.

Applications (`rowVersion` is also returned as the `ETag` header):

| Method | Path | Who | Success | Notes |
|---|---|---|---|---|
| GET | `/applications?cursor=&limit=25&includeInactive=false` | any role | `200` `Paged<Application>` | Admin sees all; others only applications owned by a team they belong to. Ordered by `name`. Inactive hidden unless `includeInactive=true` (Admin only; ignored for other roles) |
| POST | `/applications` | Admin | `201` `Application`, `Location`, `ETag` | `Idempotency-Key` honoured; `409` duplicate `code` (case-insensitive); `422` unknown `owningTeamId` / `escalationContactId` |
| GET | `/applications/{id}` | any role | `200` `Application` + `ETag` | `404` if missing **or not visible**; inactive applications are still readable |
| PUT | `/applications/{id}` | Admin, or TeamLead of the owning team | `200` `Application` + `ETag` | `If-Match` required (`428` missing, `400` malformed, `409` stale); `403` for a non-member TeamLead; only Admin may change `owningTeamId` |

### Integration (phase 2)

`POST /integrations/alerts` — called by monitoring tools, authenticated with an HMAC signature header
(`X-Signature: sha256=...`), not a user token. Body: `{ applicationCode, severity, title, description, externalId }`.
Repeated `externalId` while an incident is open adds a comment instead of creating a new incident.

---

## 6. Errors

```json
{
  "type": "https://incidenthub/errors/invalid-transition",
  "title": "Transition not allowed",
  "status": 409,
  "detail": "Cannot move INC-1042 from New to Resolved.",
  "traceId": "00-4bf92f35...-01",
  "errors": null
}
```

| Status | When | Frontend behaviour |
|---|---|---|
| 400 | Malformed request, including a malformed `If-Match` or `Idempotency-Key` | Generic error toast |
| 401 | Missing/expired token | MSAL silent refresh, then retry once |
| 403 | Role or team not allowed | Toast; hide the action |
| 404 | Not found or not visible to user | Not-found page |
| 409 | Stale `If-Match` or invalid transition | Roll back optimistic update, refetch, toast |
| 413 | File too large | Field error on the dropzone |
| 428 | Write sent without the required `If-Match` header | Client bug; send the ETag from the last read |
| 422 | Validation failed; `errors` = `{ "title": ["Title is required."] }` | Map to form fields with `setError` |
| 429 | Rate limited | Retry after `Retry-After` |
| 500 | Unexpected | Toast showing `traceId` for support |

Validation `errors` keys use the same camelCase property names as the request.

---

## 7. SignalR — `/hubs/incidents`

Strongly typed on the server (`Hub<IIncidentClient>`). Method names below are exact.

### Client → server

| Method | Args | Effect |
|---|---|---|
| `JoinDashboard` | — | Join `dashboard` group |
| `LeaveDashboard` | — | Leave it |
| `WatchIncident` | `incidentId: string` | Join `incident:{id}` (access checked) |
| `UnwatchIncident` | `incidentId: string` | Leave it |

Every connection is automatically in `user:{userId}`, and Responders in `responders:{teamId}`.

### Server → client

| Event | Payload | Sent to |
|---|---|---|
| `IncidentCreated` | `IncidentSummary` | `dashboard`, owning team |
| `IncidentUpdated` | `IncidentSummary` | `dashboard`, `incident:{id}`, team |
| `CommentAdded` | `{ incidentId: string; comment: Comment }` | `incident:{id}` (internal comments: responders only) |
| `AppStatusChanged` | `AppStatusItem` | `dashboard` |
| `DashboardChanged` | `DashboardSummary` | `dashboard` (max 1 per second) |
| `NotificationReceived` | `AppNotification` | `user:{userId}` |

```csharp
public interface IIncidentClient
{
    Task IncidentCreated(IncidentSummaryDto incident);
    Task IncidentUpdated(IncidentSummaryDto incident);
    Task CommentAdded(CommentAddedEvent e);
    Task AppStatusChanged(AppStatusItemDto item);
    Task DashboardChanged(DashboardSummaryDto summary);
    Task NotificationReceived(NotificationDto notification);
}
```

Events are sent **after** the database commit. Delivery is best-effort: on reconnect the client
refetches (`invalidateQueries`), so events never need to be replayed.

---

## 8. Example: report → resolve

```http
POST /api/v1/attachments                       → 201 { "id": "a91c...", "scanStatus": "Pending", ... }

POST /api/v1/incidents
Idempotency-Key: 6b1e2c...
{ "applicationId": "b3f1...", "environment": "Production", "severity": "High",
  "title": "Checkout fails with 500 after payment",
  "description": "<p>Since 09:40 users see an error page after paying.</p>",
  "stepsToReproduce": "1. Add item 2. Pay with card 3. Error page",
  "attachmentIds": ["a91c..."] }
→ 201 Location: /api/v1/incidents/INC-1042
  { "id": "8f2c...", "number": "INC-1042", "status": "New", "rowVersion": "AAAAAAAAB9E=", ... }
  SignalR → IncidentCreated, DashboardChanged

POST /api/v1/incidents/8f2c.../transitions
If-Match: "AAAAAAAAB9E="
{ "to": "Triaged" }
→ 200 { "status": "Triaged", "rowVersion": "AAAAAAAAB+A=", "allowedTransitions": ["InProgress","OnHold"], ... }

POST /api/v1/incidents/8f2c.../transitions
If-Match: "AAAAAAAAB+A="
{ "to": "Resolved", "note": "Payment gateway timeout raised to 30 s" }
→ 200 { "status": "Resolved", ... }
```
