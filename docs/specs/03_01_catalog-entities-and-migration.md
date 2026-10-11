# Spec 03_01: Catalog entities, EF configuration & migration

> Status: **Implemented** (integration tests need Docker; see run notes) · Source: BE-03 in docs/BACKEND-SPECS.md · Part 01 of 03

## 1. Purpose & scope

Bring the persistence model of the reference data (`Team`, `TeamMember`, `Application`, `User`) in line with
`docs/DATABASE.md`, starting from what BE-02 already built (`MonitoredApp`, `AppTeamMember`, `User`, migration
`AddUsersAndAppTeams`). No HTTP surface in this part.

In scope: domain entities and factory/mutation methods, EF configurations, one **incremental** migration, rewiring
`IAppTeamReader` to the new team model, updating BE-02 code/tests that reference renamed types.
Out of scope: endpoints (03_02, 03_03), user provisioning beyond the existing `IUserDirectory` upsert.

## 2. Dependencies

BE-01, BE-02 (existing `User`, `AppTeamMember`, `MonitoredApp`, `RedisAppTeamReader`, `IUserDirectory`, migration
`20261007051533_AddUsersAndAppTeams`). No earlier parts.

## 3. Design

### Domain (`IncidentHub.Domain`)
- `Apps/MonitoredApp` → renamed `Application`? **See Open question 1.** Until decided this spec uses `MonitoredApp` as the class name
  (per `solution-layout.md` §2) and the table/column names from DATABASE.md.
  Fields: `Name` (≤200), `Code` (≤20, unique, stored upper-case), `OwningTeamId`, `EscalationUserId?`, `Environments` (`IReadOnlyList<AppEnvironment>`),
  `IsActive` (default true), `RowVersion`. Methods: `Create(...)`, `Update(name, code, owningTeamId, escalationUserId, environments, isActive)`.
  `Environments` must be non-empty with no duplicates.
- `Teams/Team` (new folder `Teams/`): `Name` (≤200), `Email` (≤320), `TeamsChannelUrl?` (≤500, absolute https URL), `RowVersion`; `Create`, `Update`.
- `Teams/TeamMember`: composite key `(TeamId, UserId)`, `Role` (`UserRole`). Replaces `Apps/AppTeamMember` (see Open question 2).
- `Apps/AppEnvironment` enum: `Production`, `UAT`, `Development` (matches API-CONTRACT `Environment`).
- `Users/User`: add `NotificationPrefs` (JSON string, default `{"channel":"Email"}`); property naming per Open question 3.
- Entities carry no data annotations; `RowVersion` is `byte[]` on `Application`, `Team` (same pattern the Incident aggregate will use).
- Pure Domain: no EF/ASP.NET types.

### Application (`IncidentHub.Application`)
- `IAppDbContext` is not yet present; if introduced here, expose only `DbSet<MonitoredApp>`, `DbSet<Team>`, `DbSet<TeamMember>`, `DbSet<User>` + `SaveChangesAsync`
  (check `solution-layout.md` §4 note on the EF-core exception before adding any EF reference).
- `IAppTeamReader.IsMemberAsync(userId, applicationId, ct)` keeps its signature; semantics change to
  "user ∈ `TeamMembers` of the application's `OwningTeamId`".
- Add `InvalidateAsync` callers later (team-membership commands) — out of scope.

### Infrastructure
- `Persistence/Configurations/`: `MonitoredAppConfiguration` (table `Applications`), `TeamConfiguration`, `TeamMemberConfiguration`, update `UserConfiguration`; delete `AppTeamMemberConfiguration`.
  - `Applications`: unique index on `Code` (case-insensitive collation or normalized upper-case value); FK `OwningTeamId → Teams` (restrict); FK `EscalationUserId → Users` (restrict, nullable);
    `Environments` via `HasConversion` to JSON in `nvarchar(max)` with a `ValueComparer`; `IsActive` default 1; `RowVersion` `IsRowVersion()`.
  - `Teams`: `RowVersion`.
  - `TeamMembers`: PK `(TeamId, UserId)`; non-clustered index on `UserId`; `Role` stored as string (`nvarchar(30)`) via `HasConversion<string>()`.
  - `Users`: unique on `ExternalId`/`EntraObjectId`; `NotificationPrefs nvarchar(max)`.
- Migration `AddCatalog` (`dotnet ef migrations add AddCatalog -p src/IncidentHub.Infrastructure -s src/IncidentHub.Api`): renames/reshapes `MonitoredApps → Applications`, drops `AppTeamMembers`,
  creates `Teams`, `TeamMembers`. **Existing rows**: the pre-production tables are empty in every environment (confirm — Open question 4); otherwise the migration must copy data.
- `Caching/RedisAppTeamReader`: replace the `AppTeamMembers` query with a join `Applications.OwningTeamId = TeamMembers.TeamId`; cache key stays `team:{appId}:{userId}` (TTL ≤ 30 s).
  Invalidation by team now affects every app of that team (see Open question 5).

### Api / Worker
Only compile fixes for renamed types (`Hubs/IncidentsHub` group joins read the user's apps through the new join).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `contracts/swagger.json` unchanged in this part.

## 5. Acceptance criteria

- [ ] Migration `AddCatalog` applies cleanly against an **empty** database *and* on top of `AddUsersAndAppTeams`.
- [ ] Resulting schema matches `docs/DATABASE.md` for `Applications`, `Teams`, `TeamMembers`, `Users` (columns, types, FKs, indexes) — verified by a schema test.
- [ ] `Applications.Code` is unique; inserting a duplicate violates the index.
- [ ] `Environments` round-trips as a JSON array through EF (`["Production","Staging"]` style) and preserves order.
- [ ] `Application`, `Team`, `TeamMember` rows persist and reload with identical values (persistence round-trip).
- [ ] `RowVersion` changes on update and a stale update raises `DbUpdateConcurrencyException`.
- [ ] `IAppTeamReader` returns true only for members of the owning team; BE-02 team-check tests still pass.
- [ ] No data annotations on entities; one `IEntityTypeConfiguration<T>` per entity; Domain still has no EF reference.
- [ ] `dotnet build` clean (warnings as errors), `dotnet format` clean.

## 6. Test plan

Domain.UnitTests (`Apps/ApplicationTests`, `Teams/TeamTests`)
- `Create_BlankName_Throws`, `Create_CodeLowercase_StoresUpperCase`, `Create_EmptyEnvironments_Throws`, `Create_DuplicateEnvironments_Throws`, `Update_ValidValues_ChangesFields`.
- `Team.Create_InvalidTeamsChannelUrl_Throws`.

Api.IntegrationTests (`[Trait("Category","Integration")]`, Testcontainers SQL Server) — `Persistence/CatalogPersistenceTests`
- `Migrate_EmptyDatabase_Succeeds`, `Migrate_FromAddUsersAndAppTeams_Succeeds`.
- `SaveApplication_DuplicateCode_Throws`, `SaveApplication_Environments_RoundTripsAsJson`, `UpdateApplication_StaleRowVersion_ThrowsConcurrency`.
- `IsMemberAsync_UserInOwningTeam_ReturnsTrue`, `IsMemberAsync_UserInOtherTeam_ReturnsFalse`.
- Existing BE-02 integration tests updated for renamed types must stay green.

## 7. Out of scope

HTTP endpoints, DTOs, validators; team-membership management endpoints/commands; seeding; Graph/user provisioning; incident entities (BE-04).

## 8. Open questions — resolved

Decided: 1 keep class `MonitoredApp`, table `Applications`; 2 DATABASE.md wins (`Team`/`TeamMember`); 3 keep `EntraObjectId`/`LastSeenAt`, add `NotificationPrefs`; 4 tables empty, no data copy; 5 keep per-app cache keys; 6 API-CONTRACT enum, DATABASE.md example fixed; 7 `TeamMember` has no `Id`.

Original questions:

1. **Entity name**: DATABASE.md/BACKEND-SPECS say `Application` (table `Applications`); `solution-layout.md` §2 (and built code) say `MonitoredApp` (table `MonitoredApps`) to avoid colliding with the `IncidentHub.Application` namespace. Proposal: keep class `MonitoredApp`, rename table to `Applications` per DATABASE.md.
2. **Team membership model conflict**: built `AppTeamMember(ApplicationId, UserId, TeamRole Member|Lead, AddedAt)` per application (entra-id spec D5/§11) vs DATABASE.md `TeamMembers(TeamId, UserId, Role Reporter|Responder|TeamLead|Admin)` per team, with `Applications.OwningTeamId`. Proposal: DATABASE.md wins (BE-03 explicitly lists `TeamMember`); drop `AppTeamMember`/`AppTeamRole`. This changes entra-id spec §3/§11 and CLAUDE.md wording ("user belongs to the incident's application team") — confirm, then update that spec's status notes.
3. **User column names**: built `EntraObjectId`, `LastSeenAt`, no `NotificationPrefs`; DATABASE.md has `ExternalId`, `NotificationPrefs`, no `LastSeenAt`. Proposal: keep `EntraObjectId`/`LastSeenAt` (already migrated, used by `UserDirectory`), add `NotificationPrefs`, and update DATABASE.md accordingly.
4. **BE-03 says "Initial migration"** but `AddUsersAndAppTeams` already exists (BE-02). This spec makes it incremental (`AddCatalog`). Confirm no deployed database holds data in `MonitoredApps`/`AppTeamMembers`.
5. **Cache invalidation scope**: key `team:{appId}:{userId}` is per app; with team-level membership, a membership change touches many apps. Keep per-app keys and a short TTL (≤30 s), or re-key to `team:{teamId}:{userId}`? Decide before 03_02.
6. **`Environments` values**: DATABASE.md example shows `"Staging"`, API-CONTRACT enum is `Production | UAT | Development`. Spec follows API-CONTRACT; fix the DATABASE.md example.
7. **Entity base class**: built `Entity` has `Id`; `TeamMember` has a composite key and must not inherit it (or ignore `Id`). Confirm shape of `Entity`.
