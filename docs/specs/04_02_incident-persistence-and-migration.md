# Spec 04_02: Incident persistence, sequence, indexes & migration

> Status: **Implemented** · Source: BE-04 in docs/BACKEND-SPECS.md · Part 02 of 02

## 1. Purpose & scope

Persist the aggregate from 04_01 in SQL Server per `docs/DATABASE.md`: `Incidents` + `IncidentHistory` tables, the `Number`
`SEQUENCE`, the key indexes (incl. full-text and the partial unique `Fingerprint` index), one incremental migration, and
translation of the fingerprint unique-index violation into a **domain conflict** instead of a raw SQL exception.

No endpoints. No outbox (BE-05).

## 2. Dependencies

- BE-01, BE-03 (`AppDbContext`, `IAppDbContext`, `DuplicateKeyException`, migrations `AddUsersAndAppTeams`, `AddCatalog`).
- **04_01** (`Incident`, `IncidentHistory`, enums, `IncidentFingerprint`).

## 3. Design

### Application
- `Abstractions/IAppDbContext`: add `DbSet<Incident> Incidents` and `DbSet<IncidentHistory> IncidentHistory`.
- `Common/Exceptions/DuplicateIncidentException : ConflictException`-style type (Application, no provider types) — "An open incident with the same
  application and title already exists." Mapped to **409** by the existing `ConflictException` branch (make `ConflictException` unsealed, or add a branch).

### Infrastructure (`Persistence/`)
- `Sequences.cs` — `IncidentNumbers` sequence: `CREATE SEQUENCE dbo.IncidentNumbers AS int START WITH 1001 INCREMENT BY 1` (start value: OQ 2).
  Registered in `OnModelCreating` via `modelBuilder.HasSequence<int>(...)`.
- `Configurations/IncidentConfiguration` (table `Incidents`):
  - `Number`: `HasDefaultValueSql("NEXT VALUE FOR dbo.IncidentNumbers")`, `ValueGeneratedOnAdd`, unique index `IX_Incidents_Number`. The value is read back
    after `SaveChangesAsync` (needed for the BE-05 `Location` header).
  - Enums `Status`, `Severity`, `Priority`, `Source`, `CloseReason`, `Environment` → `HasConversion<string>()` with DATABASE.md lengths (`nvarchar(20)`/`(30)`).
  - Lengths: `UpdatedAt datetimeoffset` not null (added in 04_01); `Title nvarchar(300)` (domain max 200, decided in 04_01), `Description`/`StepsToReproduce nvarchar(max)`, `Fingerprint nvarchar(300)`, `ExternalId nvarchar(200)`.
  - `IsSlaBreached` default `0`; `RowVersion` `IsRowVersion()`; all times `datetimeoffset`.
  - FKs (all `Restrict`): `ApplicationId → Applications`, `ReporterId`/`AssigneeId → Users`, `AssignedTeamId → Teams`, `DuplicateOfId → Incidents` (self).
  - Indexes:
    - `IX_Incidents_Status_Severity` on `(Status, Severity)` `INCLUDE (ApplicationId, AssigneeId)` (`IncludeProperties`).
    - `IX_Incidents_Application_CreatedAt` on `(ApplicationId, CreatedAt)`.
    - `IX_Incidents_Fingerprint_Open` unique on `Fingerprint`, filter `[Status] <> 'Closed' AND [Fingerprint] IS NOT NULL`.
  - `History` navigation: `HasMany(i => i.History).WithOne().HasForeignKey(h => h.IncidentId)`, backing field access, `OnDelete(Restrict)`.
  - `Ignore(i => i.DomainEvents)`.
- `Configurations/IncidentHistoryConfiguration` (table `IncidentHistory`): `Field nvarchar(50)`, `OldValue`/`NewValue nvarchar(max)`, `Note nvarchar(1000)`,
  `ActorId → Users` (nullable: `null` = system, decided in 04_01), index `IX_IncidentHistory_Incident_CreatedAt` on `(IncidentId, CreatedAt)`.
- `AppDbContext`:
  - `DbSet<Incident> Incidents`, `DbSet<IncidentHistory> IncidentHistory`.
  - Extend the existing unique-violation catch: when the SQL error message names `IX_Incidents_Fingerprint_Open`, throw `DuplicateIncidentException`;
    otherwise keep throwing `DuplicateKeyException`.
  - **Append-only guard**: in `SaveChangesAsync`, any `IncidentHistory` entry in state `Modified` or `Deleted` throws `InvalidOperationException` before hitting the DB.
- Migration `AddIncidents` (`dotnet ef migrations add AddIncidents -p src/IncidentHub.Infrastructure -s src/IncidentHub.Api`):
  sequence → `Incidents` → `IncidentHistory`, then the full-text part as raw SQL with `suppressTransaction: true`:
  `CREATE FULLTEXT CATALOG ftIncidentHub` (if not exists) and `CREATE FULLTEXT INDEX ON dbo.Incidents(Title, Description) KEY INDEX PK_Incidents ON ftIncidentHub WITH CHANGE_TRACKING AUTO`;
  `Down` drops them in reverse. **Requires an SQL Server image with Full-Text Search** — see OQ 1.

### Api / Worker
Nothing beyond what 04_01 adds to `ProblemDetailsMiddleware`.

### Ops
- `docker-compose.yml` and the Testcontainers `MsSqlBuilder` in `ApiFactory` use an image with full-text installed (OQ 1).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md`/`swagger.json` unchanged. `docs/DATABASE.md` is updated for the columns decided in 04_01 OQ 1, 2, 5, 6, 7, 11
(`Source`/`CloseReason` values, `UpdatedAt`, `DuplicateOfId`, `ActorId` nullability).

## 5. Acceptance criteria

- [ ] `AddIncidents` applies cleanly against an **empty** database and on top of `AddCatalog`; `Down` reverts it.
- [ ] Resulting schema matches DATABASE.md for `Incidents` and `IncidentHistory` (columns, types, nullability, FKs, all listed indexes incl. the filter and `INCLUDE` columns, full-text index).
- [ ] Saving two incidents assigns consecutive `Number`s from the sequence; `Number` is populated on the entity after `SaveChangesAsync`.
- [ ] An incident round-trips with identical values, including enum strings and history rows in order.
- [ ] Two **open** incidents with the same `Fingerprint` cannot both be saved: the second save throws `DuplicateIncidentException` (not `DbUpdateException`/`SqlException`), surfaced as 409.
- [ ] A closed incident does not block a new open incident with the same `Fingerprint`.
- [ ] A transition persisted through EF writes the incident update and its `IncidentHistory` row in one `SaveChangesAsync`.
- [ ] Modifying or deleting a tracked `IncidentHistory` row throws before any SQL is sent.
- [ ] Stale `RowVersion` on `Incident` raises `DbUpdateConcurrencyException`.
- [ ] Full-text query (`CONTAINS(Title, ...)`) works against the migrated database.
- [ ] No data annotations; one `IEntityTypeConfiguration<T>` per entity; Application references core EF only (ADR 0001); build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Api.IntegrationTests/Persistence/IncidentPersistenceTests` — `[Trait("Category","Integration")]`, shared SQL Server container fixture:
- `Migrate_EmptyDatabase_Succeeds`, `Migrate_FromAddCatalog_Succeeds`, `MigrateDown_ToAddCatalog_Succeeds`.
- `Schema_Incidents_MatchesDatabaseDoc` (queries `sys.indexes`/`sys.index_columns`/`sys.fulltext_indexes`).
- `SaveIncident_Twice_AssignsConsecutiveNumbers`, `SaveIncident_RoundTrip_PreservesValuesAndHistory`.
- `SaveIncident_SameFingerprintBothOpen_ThrowsDuplicateIncident`, `SaveIncident_SameFingerprintFirstClosed_Succeeds`.
- `TransitionTo_Persisted_WritesIncidentAndHistoryTogether`.
- `UpdateIncident_StaleRowVersion_ThrowsConcurrency`.
- `ModifyHistoryRow_Tracked_ThrowsBeforeSave`.
- `FullTextSearch_Title_ReturnsMatch` (wait for population, short poll with timeout).

`tests/IncidentHub.Api.UnitTests/Errors/ProblemDetailsMiddlewareTests`: `InvokeAsync_DuplicateIncident_Returns409`.

## 7. Out of scope

- `OutboxMessages` table and writing outbox rows in the same transaction → BE-05.
- `Comments`, `Attachments` tables → BE-08. `Notifications` → BE-14. `SlaPolicies` → BE-12. `IncidentDailyStats` → BE-15.
- Query handlers/list filters using these indexes → BE-06 (full-text similar search), BE-07 (list/detail).
- Seeding data for the 50k-row performance test → BE-06.

## 8. Open questions — resolved

All decided with the user on 2026-10-11:

1. Full-text: custom SQL Server image `docker/mssql-fts` (SQL Server 2022 + `mssql-server-fts`), used by `docker-compose.yml` and built by Testcontainers (`PullPolicy.Missing`, cached). The migration always creates the catalog and index.
2. Sequence `IncidentNumbers` starts at 1001.
3. Every incident gets a `Fingerprint`; a second open incident with the same app + normalized title is blocked (409).
4. The violation is detected by index name `IX_Incidents_Fingerprint_Open` in `AppDbContext`. A friendlier pre-check (returning the existing number) is left to the BE-05 handler.
5. All new FKs are `Restrict` (including `IncidentHistory → Incidents` and the `DuplicateOfId` self-FK).

Also decided: `DuplicateIncidentException : ConflictException` (`ConflictException` unsealed), so the existing 409 branch handles it; the append-only guard lives in the `AppDbContext` `SaveChanges`/`SaveChangesAsync` overrides.

## 9. Implementation notes

- `Incident.Id` and `IncidentHistory.Id` are `ValueGeneratedNever()`: the domain assigns the Guid, and without it EF would treat a new history row reached through `Incident.History` as already existing.
- The full-text statements run with `suppressTransaction: true` (a catalog cannot be created inside a user transaction).
- The Dockerfile adds the Microsoft package repository only if `mssql-server-fts` is not found in the base image's apt sources.
- `ApiFactory` runs the API against its own database (`incidenthub_tests`), not `master`: SQL Server refuses full-text indexes in master, tempdb and model.
