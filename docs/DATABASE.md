# IncidentHub — Database Schema

_Source: System Design — Service Incident & Monitoring Dashboard. SQL Server, EF Core 8 (code-first)._
_All timestamps are UTC (`datetimeoffset`). All primary keys are `uniqueidentifier` (Guid) unless noted._

---

## Entity-relationship overview

```
Team ──┬──< TeamMember >──┬── User
       │                   │
       └──< Application    ├──< Incident >── IncidentHistory
                            │        │
                            │        ├──< Comment
                            │        ├──< Attachment
                            │        └──< Notification
                            │
                            └──< IncidentDailyStats

SlaPolicy (standalone, keyed by Severity)
OutboxMessage (standalone, written in the same transaction as the triggering change)
```

---

## Tables

### `Applications`
The services/apps that incidents are reported against. (C# entity: `MonitoredApp`, to avoid colliding with the `IncidentHub.Application` namespace.)

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `Name` | `nvarchar(200)` | not null |
| `Code` | `nvarchar(20)` | not null, unique — short slug used in URLs/filters |
| `OwningTeamId` | `uniqueidentifier` | FK → `Teams.Id`, not null |
| `EscalationUserId` | `uniqueidentifier` | FK → `Users.Id`, nullable |
| `Environments` | `nvarchar(max)` | JSON array, e.g. `["Production","UAT"]` |
| `IsActive` | `bit` | not null, default `1` |
| `RowVersion` | `rowversion` | concurrency token |

**Indexes:** unique on `Code`.

---

### `Teams`
Owning teams for applications; notification routing target.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `Name` | `nvarchar(200)` | not null |
| `Email` | `nvarchar(320)` | not null |
| `TeamsChannelUrl` | `nvarchar(500)` | nullable — MS Teams/Slack webhook |
| `RowVersion` | `rowversion` | concurrency token |

---

### `TeamMembers`
Join table: team membership + role within that team.

| Column | Type | Notes |
|---|---|---|
| `TeamId` | `uniqueidentifier` | FK → `Teams.Id`, PK (composite) |
| `UserId` | `uniqueidentifier` | FK → `Users.Id`, PK (composite) |
| `Role` | `nvarchar(30)` | `Reporter` \| `Responder` \| `TeamLead` \| `Admin` |

**Indexes:** PK on `(TeamId, UserId)`; non-clustered on `UserId` (lookup "my teams").

---

### `Users`
Local projection of Entra ID identities — never the source of truth for auth, just a profile cache.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `EntraObjectId` | `nvarchar(64)` | not null, unique — Entra Object ID |
| `DisplayName` | `nvarchar(200)` | not null |
| `Email` | `nvarchar(320)` | not null |
| `NotificationPrefs` | `nvarchar(max)` | not null, JSON, default `{"channel":"Email"}` |
| `LastSeenAt` | `datetimeoffset` | not null — updated on each sign-in |

**Indexes:** unique on `EntraObjectId`.

---

### `Incidents`
The core aggregate.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `Number` | `int` | not null, unique — from SQL `SEQUENCE dbo.IncidentNumbers` (starts at 1001), displayed as `INC-{Number}` |
| `ApplicationId` | `uniqueidentifier` | FK → `Applications.Id`, not null |
| `Environment` | `nvarchar(30)` | not null, e.g. `Production` |
| `Title` | `nvarchar(300)` | not null |
| `Description` | `nvarchar(max)` | not null, sanitized HTML |
| `StepsToReproduce` | `nvarchar(max)` | nullable |
| `Severity` | `nvarchar(20)` | `Critical` \| `High` \| `Medium` \| `Low` |
| `Priority` | `nvarchar(20)` | nullable, independent of severity |
| `Status` | `nvarchar(20)` | `New` \| `Triaged` \| `InProgress` \| `OnHold` \| `Resolved` \| `Reopened` \| `Closed` — **set only via `Incident.TransitionTo`** |
| `Source` | `nvarchar(20)` | `Manual` \| `Webhook` |
| `ReporterId` | `uniqueidentifier` | FK → `Users.Id`, not null |
| `AssigneeId` | `uniqueidentifier` | FK → `Users.Id`, nullable |
| `AssignedTeamId` | `uniqueidentifier` | FK → `Teams.Id`, nullable |
| `Fingerprint` | `nvarchar(300)` | nullable — dedup hash (app + normalized title) |
| `ExternalId` | `nvarchar(200)` | nullable — for webhook-sourced incidents, idempotency key for upserts |
| `AckDueAt` | `datetimeoffset` | set at creation/severity change from `SlaPolicy` |
| `ResolveDueAt` | `datetimeoffset` | set at creation/severity change from `SlaPolicy` |
| `IsSlaBreached` | `bit` | not null, default `0`, set by `SlaMonitor` |
| `AcknowledgedAt` | `datetimeoffset` | nullable |
| `ResolvedAt` | `datetimeoffset` | nullable |
| `ClosedAt` | `datetimeoffset` | nullable |
| `CloseReason` | `nvarchar(30)` | nullable — `Fixed` \| `Duplicate` \| `Invalid` \| `CannotReproduce` |
| `DuplicateOfId` | `uniqueidentifier` | FK → `Incidents.Id` (self), nullable — set only when `CloseReason = Duplicate` |
| `CreatedAt` | `datetimeoffset` | not null |
| `UpdatedAt` | `datetimeoffset` | not null — set on every change to the incident; feeds `updatedAt` and the `-updatedAt` sort |
| `RowVersion` | `rowversion` | concurrency token — required for `If-Match` on every write |

**Indexes:**
- `IX_Incidents_Status_Severity` on `(Status, Severity)` `INCLUDE (ApplicationId, AssigneeId)` — dashboard/list filtering
- `IX_Incidents_Application_CreatedAt` on `(ApplicationId, CreatedAt)` — per-app history/trend queries
- Full-text index on `Title`, `Description` (catalog `ftIncidentHub`, key index `PK_Incidents`) — search and duplicate hints; needs SQL Server with Full-Text Search (`docker/mssql-fts` locally)
- Unique filtered index on `Fingerprint` `WHERE Status <> 'Closed'` — prevents duplicate open incidents
- Unique on `Number`

---

### `IncidentHistory`
Append-only audit trail. Never updated or deleted.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `IncidentId` | `uniqueidentifier` | FK → `Incidents.Id`, not null |
| `Field` | `nvarchar(50)` | the entity property name, e.g. `Status`, `Severity`, `AssigneeId`; the API maps it to a display name. `Comment` / `InternalComment` mark a new comment (`NewValue` = comment id); `InternalComment` rows are hidden from non-responders |
| `OldValue` | `nvarchar(max)` | nullable |
| `NewValue` | `nvarchar(max)` | nullable |
| `Note` | `nvarchar(1000)` | nullable — optional note on a transition |
| `ActorId` | `uniqueidentifier` | FK → `Users.Id`, nullable — `null` = the system (SLA job, webhook) |
| `CreatedAt` | `datetimeoffset` | not null |

**Indexes:** `(IncidentId, CreatedAt)` — paged, newest-first reads.

---

### `Comments`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `IncidentId` | `uniqueidentifier` | FK → `Incidents.Id`, not null |
| `AuthorId` | `uniqueidentifier` | FK → `Users.Id`, not null |
| `Body` | `nvarchar(max)` | not null, sanitized HTML |
| `IsInternal` | `bit` | not null, default `0` — **never returned to Reporter role** |
| `CreatedAt` | `datetimeoffset` | not null |
| `EditedAt` | `datetimeoffset` | nullable |

**Indexes:** `(IncidentId, CreatedAt)`.

---

### `Attachments`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `IncidentId` | `uniqueidentifier` | FK → `Incidents.Id`, **nullable** — null from the upload (`POST /attachments`) until a report or comment links it; the link is a concurrency token, so a file links once even under parallel requests |
| `CommentId` | `uniqueidentifier` | FK → `Comments.Id`, nullable — set when the attachment is linked to a comment |
| `UploadedById` | `uniqueidentifier` | FK → `Users.Id`, not null |
| `FileName` | `nvarchar(300)` | not null |
| `ContentType` | `nvarchar(100)` | not null |
| `SizeBytes` | `bigint` | not null, ≤ 25 MB enforced in app layer |
| `BlobPath` | `nvarchar(500)` | not null — path in Blob Storage, never exposed directly |
| `ScanStatus` | `nvarchar(20)` | `Pending` \| `Clean` \| `Infected` |
| `ScanAttempts` | `int` | not null, default `0` — failed scan attempts while `Pending`; the scan job claims the fewest first |
| `CreatedAt` | `datetimeoffset` | not null |

**Indexes:** `IX_Attachments_IncidentId` on `(IncidentId)`; `IX_Attachments_CommentId` on `(CommentId)`; filtered `IX_Attachments_Unlinked` on `(UploadedById, CreatedAt) WHERE IncidentId IS NULL` (orphan cleanup); filtered `IX_Attachments_Pending` on `(ScanAttempts, CreatedAt) WHERE ScanStatus = 'Pending'` (the scan job's claim query and its stuck-attachment check).

Visibility: an unlinked attachment is visible to its uploader only; a linked one to the members of the incident's application team (an attachment of an internal comment not to Reporters).

---

### `Notifications`
In-app notification feed (phase 2, `BE-14`).

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `UserId` | `uniqueidentifier` | FK → `Users.Id`, not null |
| `Type` | `nvarchar(30)` | `IncidentAssigned` \| `SlaWarning` \| `SlaBreached` \| `CommentAdded` \| etc. |
| `IncidentId` | `uniqueidentifier` | FK → `Incidents.Id`, nullable |
| `Payload` | `nvarchar(max)` | JSON — rendering data for the notification |
| `IsRead` | `bit` | not null, default `0` |
| `CreatedAt` | `datetimeoffset` | not null |

**Indexes:** `(UserId, IsRead, CreatedAt)` — unread-list queries.

---

### `SlaPolicies`
Phase 2 (`BE-12`). One row per severity.

| Column | Type | Notes |
|---|---|---|
| `Severity` | `nvarchar(20)` | PK — `Critical` \| `High` \| `Medium` \| `Low` |
| `AckMinutes` | `int` | not null |
| `ResolveMinutes` | `int` | not null |
| `EscalateAtPercent` | `int` | not null — e.g. `80` |

---

### `IncidentDailyStats`
Phase 2 (`BE-15`). Pre-aggregated rollup, written nightly — dashboard trend charts read this, never raw incidents.

| Column | Type | Notes |
|---|---|---|
| `Date` | `date` | PK (composite) |
| `ApplicationId` | `uniqueidentifier` | FK → `Applications.Id`, PK (composite) |
| `Reported` | `int` | not null |
| `Resolved` | `int` | not null |
| `AvgMttaMinutes` | `decimal(10,2)` | nullable |
| `AvgMttrMinutes` | `decimal(10,2)` | nullable |

**Indexes:** PK on `(Date, ApplicationId)`.

---

### `OutboxMessages`
Transactional outbox — written in the same `SaveChangesAsync` as the triggering domain change; dispatched asynchronously by the Worker.

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `Type` | `nvarchar(50)` | e.g. `IncidentReported`, `SlaBreached` |
| `Payload` | `nvarchar(max)` | JSON |
| `OccurredAt` | `datetimeoffset` | not null |
| `ProcessedAt` | `datetimeoffset` | nullable — null = undispatched |
| `Attempts` | `int` | not null, default `0` |
| `Error` | `nvarchar(max)` | nullable — last failure message |

**Indexes:** `IX_OutboxMessages_Unprocessed` on `ProcessedAt` `INCLUDE (OccurredAt)` `WHERE ProcessedAt IS NULL` — dispatcher poll query uses `UPDLOCK, READPAST` against this and orders by `OccurredAt`.

---

## Rules that aren't visible in the schema alone

- **`Incidents.Status` has no public setter at the application layer** — it only changes through `Incident.TransitionTo(newStatus, actor, note, at, ...)`, which also writes the matching `IncidentHistory` row in the same call. Never issue a raw `UPDATE Incidents SET Status = ...`.
- **`AppStatus` (Operational/Degraded/Outage) is not a column anywhere.** It's computed from open `Incidents` (severity + environment) by `AppStatusCalculator` and cached in Redis — there is intentionally no `Applications.Status` column to keep it from being set directly.
- **`Fingerprint`** is computed by the app (not a DB computed column) from `ApplicationId` + a normalized `Title`, so the partial unique index can prevent duplicate *open* incidents for the same thing.
- **Concurrency:** every mutable aggregate (`Incidents`, `Applications`, `Teams`) carries `RowVersion`; the API requires `If-Match` on writes and returns `409` on mismatch — don't drop `RowVersion` from a DTO just because a screen doesn't show it.
- **Soft vs hard delete:** nothing in this schema is hard-deleted except never-linked uploads (`Attachments` with `IncidentId IS NULL`, row and blob), which the Worker removes 24 hours after upload. `Applications.IsActive` is the only "removal" — history and incidents are retained.

---

## Migration ordering (first migration)
1. `Teams`, `Users` (no dependencies)
2. `Applications` (depends on `Teams`, `Users`)
3. `TeamMembers` (depends on `Teams`, `Users`)
4. `Incidents` sequence + table (depends on `Applications`, `Users`, `Teams`)
5. `IncidentHistory`, `Comments`, `Attachments`, `Notifications` (depend on `Incidents`, `Users`)
6. `SlaPolicies`, `OutboxMessages`, `IncidentDailyStats` (standalone / depend on `Applications`)

See `backend/CLAUDE.md` → **Data access** for EF Core configuration conventions (one `IEntityTypeConfiguration<T>` per entity, no data annotations on entities, migrations applied via pipeline bundle only).
