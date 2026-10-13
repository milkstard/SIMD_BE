# Spec 09_01: Hub contract, groups & access checks

> Status: **Draft** · Source: BE-09 in docs/BACKEND-SPECS.md · Part 01 of 02

## 1. Purpose & scope

Bring `/hubs/incidents` in line with API-CONTRACT §7: the full strongly typed `IIncidentClient`, the contract's group model, and
team-checked group management (`JoinDashboard`, `LeaveDashboard`, `WatchIncident`, `UnwatchIncident`). Nothing is broadcast yet (09_02).

In scope:
- Replace the BE-02 stub (`app:{id}` / `app:{id}:responders` groups, `CommentAdded(CommentDto)`) with the contract model (decided 2026-10-13: contract wins).
- `IIncidentClient` with all six methods; payload records for the events.
- Automatic membership of `user:{userId}` and the team groups on connect.
- Redis backplane stays as wired in `Program.cs` (verify only).

## 2. Dependencies

- BE-02 (`Policies.CanViewIncident`, `AuthClaims`, `IAppTeamReader`), BE-03 (`TeamMembers`, `Applications.OwningTeamId`), 07_01 (`IncidentSummaryDto`, visibility rule: incidents of applications owned by a team the caller belongs to, Admin included).
- 08_01 introduces the comment DTO used by `CommentAdded`; if it is not built yet, the payload record references the DTO name from that spec.

## 3. Design

### Application
- `Abstractions/IAppTeamReader`: add `GetTeamIdsForUserAsync(Guid userId, CancellationToken)` (uncached, like `GetApplicationIdsForUserAsync`). `GetOwningTeamIdAsync` moved to 09_02, where it is first used.
- A small `IIncidentAccess` query (or reuse `IncidentWriteGuard`'s visibility check) answering "may this user see incident `{id}`": incident exists and the caller is a member of its application's team. Unknown and not visible are indistinguishable.

### Api
`Hubs/`
- `IIncidentClient` (exact names, no string method names anywhere):
  `IncidentCreated(IncidentSummaryDto)`, `IncidentUpdated(IncidentSummaryDto)`, `CommentAdded(CommentAddedEvent)`, `AppStatusChanged(AppStatusItemDto)`, `DashboardChanged(DashboardSummaryDto)`, `NotificationReceived(NotificationDto)`.
  `CommentAddedEvent(Guid IncidentId, CommentDto Comment)`. `AppStatusItemDto`, `DashboardSummaryDto`, `NotificationDto` are declared here as the contract shapes with no producer yet (producers: BE-11, BE-14).
- `HubGroups`: `Dashboard(teamId)` = `dashboard:{teamId:N}`, `Team(teamId)` = `team:{teamId:N}`, `Responders(teamId)` = `responders:{teamId:N}`, `Incident(incidentId)` = `incident:{incidentId:N}`, `User(userId)` = `user:{userId:N}` (see OQ 1 and 2).
- `IncidentsHub : Hub<IIncidentClient>`, `[Authorize(Policy = Policies.CanViewIncident)]`:
  - `OnConnectedAsync`: add to `user:{id}`, to `team:{teamId}` for every team of the user and, for Responder-level roles (Responder, TeamLead, Admin from the token), to `responders:{teamId}`. No user id → abort (as today).
  - `JoinDashboard()` / `LeaveDashboard()`: add/remove `dashboard:{teamId}` for each of the caller's teams.
  - `WatchIncident(Guid incidentId)`: visibility check; not visible → `HubException("Incident not found.")` (same message for unknown and forbidden), no group change. Visible → join `incident:{id}`.
  - `UnwatchIncident(Guid incidentId)`: leave the group (no access check needed to leave).
  - Hub methods hold no business logic: they call the Application queries and `Groups`.
- Group membership is fixed at connect time for `team:` / `responders:` / `user:`; a membership change takes effect on reconnect (documented; BE-03's "owning team changes" case is the same, see OQ 3).

### Infrastructure
`RedisAppTeamReader`: implement `GetTeamIdsForUserAsync`.

### Worker
None.

## 4. Endpoints & DTOs

SignalR only; no REST change. API-CONTRACT §7 must be updated: `dashboard:{teamId}` / `team:{teamId}` group names and their meaning (OQ 1, 2), `LeaveDashboard`, the `HubException` for `WatchIncident`, and group membership refreshed on reconnect. `contracts/swagger.json` is unaffected.

## 5. Acceptance criteria

- [ ] Hub is `Hub<IIncidentClient>`; `IIncidentClient` has exactly the six contract methods; no `SendAsync("name")` anywhere in the code base.
- [ ] A user without access to an incident's application is rejected from `WatchIncident` (same error for an unknown id); a member joins and can leave.
- [ ] `JoinDashboard` joins only the dashboards of the caller's own teams; a user in no team joins nothing.
- [ ] Every connection is in `user:{userId}`; team groups follow the user's teams; `responders:` groups only for Responder-level roles.
- [ ] A Reporter-role connection is never in a `responders:` group.
- [ ] Unauthenticated connection and one without the required scope are refused (existing BE-02 behaviour kept).
- [ ] Redis backplane configured (`AddStackExchangeRedis`) with `AbortOnConnectFail = false`.
- [ ] Contract doc updated in the same change; build + `dotnet format` clean.

## 6. Test plan

`tests/IncidentHub.Api.IntegrationTests/Hubs/` — `[Trait("Category","Integration")]`, `ApiFactory`, real `HubConnection` (as `IncidentsHubAuthTests`):
- `Connect_Member_IsInUserAndTeamGroups`, `Connect_Reporter_NotInRespondersGroup`, `Connect_Responder_InRespondersGroup`
- `WatchIncident_Member_Succeeds`, `WatchIncident_NonMember_ThrowsHubException`, `WatchIncident_UnknownId_ThrowsSameException`, `UnwatchIncident_AfterWatch_LeavesGroup`
- `JoinDashboard_JoinsOnlyOwnTeams`, `LeaveDashboard_LeavesGroups`
- `Connect_NoToken_Rejected`, `Connect_WrongScope_Rejected` (adapt the existing auth tests)

Group membership is asserted by sending a probe through `IHubContext<IncidentsHub, IIncidentClient>` to the group and checking which test clients receive it.

`tests/IncidentHub.Application.UnitTests`: handler/query tests for the incident-visibility check (member, non-member, unknown id).
`tests/IncidentHub.Architecture` / `Api.UnitTests`: `IIncidentClient_HasOnlyContractMethods`.

## 7. Out of scope

- Broadcasting any event, the post-commit dispatcher, `DashboardChanged` throttling → 09_02.
- Producing `AppStatusChanged` / `DashboardChanged` payloads → BE-11; `NotificationReceived` → BE-14.
- Live regrouping when team membership or an application's owning team changes (takes effect on reconnect).

## 8. Decisions

Decided with the user on 2026-10-13.

1. **`dashboard` group scope:** `JoinDashboard` joins `dashboard:{teamId}` per own team (no cross-team leak); method names unchanged.
2. **Owning-team recipients:** new `team:{teamId}` group for all members, next to `responders:{teamId}`. Additive.
3. **Stale membership:** accepted until reconnect; token expiry already closes the connection.
4. **Role source:** the responders group follows the token role (Responder, TeamLead, Admin), as the stub and every REST check do.
5. **`GetOwningTeamIdAsync`** moves to 09_02.
