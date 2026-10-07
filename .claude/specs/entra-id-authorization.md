# Spec: Entra ID authentication & authorization

> Status: **Draft — open questions resolved (§16); not implemented.** Source of truth for rules is [`CLAUDE.md`](../../CLAUDE.md) (Security rules,
> Core domain rules); file placement follows [`solution-layout.md`](solution-layout.md). If this spec and CLAUDE.md
> disagree, CLAUDE.md wins — fix the spec.

## 1. Purpose & scope

Secure `IncidentHub.Api` (REST + SignalR) with Microsoft Entra ID (OIDC / OAuth 2.0 JWT bearer), map Entra groups to
the four app roles, and enforce authorization with **policies + a resource check on the application team**.

In scope: token validation, role mapping, policies, team-membership resource check, `ICurrentUser`, SignalR auth and
group membership, internal-comment protection, config, tests.
Out of scope: Graph group-overage resolution (deferred), B2B guests, the frontend sign-in flow (MSAL), user provisioning/SCIM, Worker authentication (it has no inbound
endpoints; it talks to Entra only if it needs Graph — see §12), authorization of Azure resources (Key Vault, Blob) which
uses managed identity.

## 2. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | API is a **protected resource only**: `AddMicrosoftIdentityWebApi` (`Microsoft.Identity.Web`, already referenced). No cookies, no interactive login in the API. | SPA acquires tokens; API validates them. |
| D2 | Roles come from **Entra security groups** (per CLAUDE.md), mapped to `Reporter`, `Responder`, `TeamLead`, `Admin` via configuration (group object ID → role). | Groups are how the org already administers access. |
| D3 | Tokens carry the `groups` claim (object IDs). **Group overage** (>200 groups → `_claim_names`/`hasgroups`) is **deferred**: when detected, log a `Warning` and grant no roles (fail closed). Graph resolution is a future spec. Mitigate by assigning only the four IncidentHub groups to the enterprise app and using "groups assigned to the application" in the token config. | Overage silently drops groups; failing closed is safe and visible. |
| D4 | Role mapping happens once per request in `IClaimsTransformation`, adding standard `ClaimTypes.Role` claims. Policies only ever look at roles, never at group IDs. | Single mapping point; policies stay readable. |
| D5 | **Team membership** (user ↔ `MonitoredApp`) is data in SQL (`AppTeamMember`), not an Entra concept. The resource check reads it; it is cached briefly in Redis. | Teams are app-specific and change often. |
| D6 | Admin is an explicit role and **not** a bypass of the team check for incident actions. Admin bypasses only for application management and the dashboard (§6). | Least privilege; auditable. |
| D8 | **Single tenant, members only.** `tid` must equal the configured tenant; B2B guests are not supported. | Internal tool; avoids guest `oid`/email mapping. |
| D7 | Authorization failures return RFC 7807 via `ProblemDetailsMiddleware`: 401 unauthenticated, 403 forbidden (incl. forbidden transition). | CLAUDE.md error rules. |

## 3. Layering (respect dependency direction)

| Concern | Project / folder |
|---|---|
| `ICurrentUser` (`UserId`, `Email`, `DisplayName`, `Roles`, `IsInRole`) and `IAppTeamReader` (`IsMemberAsync(userId, appId, ct)`) interfaces | `Application/Abstractions/` |
| `Role` enum / `Actor` value object passed to `Incident.TransitionTo(newStatus, actor, note)` | `Domain/Users/` (no ASP.NET types) |
| `AppTeamMember` entity + EF configuration; `AppTeamReader` (SQL + Redis cache) | `Domain/Apps/`, `Infrastructure/Persistence/Configurations/`, `Infrastructure/Caching/` |
| JWT setup, `Policies`, `AppTeamRequirement`, `AppTeamAuthorizationHandler`, `GroupRoleClaimsTransformation`, `CurrentUser` | `Api/Auth/` |

Domain and Application never reference ASP.NET, `ClaimsPrincipal` or Microsoft.Identity types. Handlers get the actor
via `ICurrentUser`; the domain state machine validates *who* may transition (role per the workflow table) and the
policy layer validates *that the caller may attempt it at all* — both must hold.

## 4. Entra app registrations (manual / IaC, documented here)

1. **API registration** `IncidentHub API`: Application ID URI `api://<api-client-id>`; expose scope `access_as_user`;
   `accessTokenAcceptedVersion = 2`; token configuration → add optional `groups` claim (security groups, emit group IDs).
2. **SPA registration** `IncidentHub Web`: redirect URIs per environment; API permission `access_as_user`; admin-consented.
3. **Groups**: one security group per role (`IncidentHub-Reporters`, `-Responders`, `-TeamLeads`, `-Admins`), assigned to the
   API's enterprise app ("assignment required" = yes). Users may be in several; effective roles are the union.
4. In token configuration, emit only groups **assigned to the application** to keep the `groups` claim small (overage handling is deferred, D3).

## 5. Configuration

`appsettings.json` (non-secret only):

```json
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "<tenant-id>",
  "ClientId": "<api-client-id>",
  "Audience": "api://<api-client-id>"
},
"Authorization": {
  "GroupRoleMap": {
    "<reporters-group-id>": "Reporter",
    "<responders-group-id>": "Responder",
    "<teamleads-group-id>": "TeamLead",
    "<admins-group-id>": "Admin"
  }
}
```

Secrets (`AzureAd:ClientCredentials`) → user-secrets locally, Key Vault in Azure. Bind via `IOptions` with
`ValidateOnStart` — startup fails if `TenantId`/`ClientId` are missing or `GroupRoleMap` is empty.
Group IDs are GUIDs, not secrets, but differ per environment (`appsettings.{Env}.json`).

## 6. Roles & policies

Roles (hierarchy is **not** implicit — each policy lists what it accepts): `Reporter`, `Responder`, `TeamLead`, `Admin`.

`Api/Auth/Policies.cs` holds the constants; registration in one `AddIncidentHubAuthorization()` extension.
Fallback policy = **require authenticated user** (everything is protected unless `[AllowAnonymous]`, e.g. health checks).

| Policy | Roles accepted | Team check | Used by |
|---|---|---|---|
| `CanReportIncident` | Reporter, Responder, TeamLead, Admin | Member of target app's team (always required) | `POST /incidents` |
| `CanViewIncident` | any authenticated role | Member of the incident's app team (Reporters: also their own incidents) | `GET /incidents/*`, `/incidents/{n}/history` |
| `CanTransitionIncident` | Reporter, Responder, TeamLead, Admin | Member of app team; **fine-grained role per transition is enforced by `Incident.TransitionTo`** (Assignee/Reporter/Responder rules) | `POST /incidents/{n}/transitions` |
| `CanAssignIncident` | Responder, TeamLead, Admin | Member | assign endpoint |
| `CanCommentInternally` | Responder, TeamLead, Admin | Member | create/read internal comments |
| `CanManageApplications` | TeamLead, Admin | TeamLead: own app only; Admin: any | application + team + SLA policy endpoints |
| `CanViewDashboard` | any authenticated role | Results filtered to apps the user belongs to (Admin: all) | `/dashboard/*` |

Policies are applied with `[Authorize(Policy = Policies.X)]` on the action. Where the policy has a team check, the
controller/handler passes the resource through `IAuthorizationService.AuthorizeAsync(user, resource, requirement)`
— **no ad-hoc `if (user.IsInRole…)` in controllers or handlers.** Admin bypasses the team check only for
`CanManageApplications` and `CanViewDashboard`; for incident actions Admin must still be a team member (no global bypass — decided; an ADR is required to change this).

### 6.1 Resource check (`AppTeamRequirement` / handler)

`AppTeamAuthorizationHandler : AuthorizationHandler<AppTeamRequirement, IAppScoped>` where `IAppScoped` exposes
`ApplicationId` (implemented by a small authorization-resource record the handler builds — **not** the EF entity).
It calls `IAppTeamReader.IsMemberAsync` (Redis cache, TTL ≤ 30 s, keyed `team:{appId}:{userId}`; invalidated on team
membership change). Not a member → `context.Fail()` → 403.

**Existence hiding:** for `GET`s on an incident the caller can't see, return **404**, not 403 (don't leak INC numbers).
Write attempts on a visible incident without permission → 403.

## 7. Claims & `ICurrentUser`

- User key = `oid` claim (stable per tenant; **not** `sub`, **not** email). Map `oid` → internal `User.Id` on first
  sight (upsert `User` row with display name/email; `tid` must equal the configured tenant — enforced by token validation).
- `GroupRoleClaimsTransformation` (scoped, idempotent — checks a marker claim so it doesn't double-add):
  1. read `groups` claim; if absent and overage is indicated → log `Warning` ("Group overage for {UserId}; no roles granted") and grant none (deferred, D3);
  2. map through `GroupRoleMap`; add `ClaimTypes.Role` per match;
  3. user with **no mapped role** is authenticated but has no roles → fallback policy passes, every role-based policy fails (403).
- `CurrentUser : ICurrentUser` reads from `IHttpContextAccessor`; throws if used on an unauthenticated request.
- Use `TimeProvider` where time matters (cache expiry); no `DateTime.UtcNow`.

## 8. JWT validation

`AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))` with:
- Valid audiences: `api://<client-id>` and `<client-id>`; issuer validated against tenant v2 endpoint; lifetime + signature on (defaults).
- Scope check: controllers require scope `access_as_user` (`RequiredScope` / scope claim in the fallback policy).
- `MapInboundClaims = false` so claim names are the raw JWT names (`oid`, `groups`, `roles`); role claim type set to `ClaimTypes.Role`
  after transformation — one place, covered by tests.
- Clock skew ≤ 2 min. Log failures at `Warning` with structured templates, **never log the token**.
- `[AllowAnonymous]` only for health/readiness endpoints.

## 9. SignalR (`/hubs/incidents`)

- Hub is `[Authorize]`. Browsers cannot set headers on WebSocket; accept the JWT from the `access_token` query string
  **only for paths starting with `/hubs`** (`JwtBearerEvents.OnMessageReceived`). Ensure the query string is not logged
  (redact in request logging/OpenTelemetry).
- On connect, join groups: `app:{applicationId}` for every app the user belongs to, plus `app:{applicationId}:responders`
  when the user has a Responder-level role. Membership is computed at connect time via `IAppTeamReader` — if team membership
  changes, the user must reconnect (document in the frontend contract; optionally force-disconnect on removal).
- `CommentAdded` for `IsInternal = true` is sent **only** to `app:{id}:responders`; public comments to `app:{id}`.
  Reporters never join the responders group. (CLAUDE.md invariant.)
- Hub methods that a client can call re-check policy via `IAuthorizationService`; server → client broadcasts happen
  post-commit as already specified.
- Token expiry on a long-lived connection: the client reconnects with a fresh token (`accessTokenFactory`); server closes
  the connection when the token expires (`CloseOnAuthenticationExpiration = true`).

## 10. REST behavior & OpenAPI

| Situation | Status | Body |
|---|---|---|
| No / invalid / expired token | 401 + `WWW-Authenticate: Bearer` | ProblemDetails + `traceId` |
| Valid token, missing role/scope, or not on the app team (write) | 403 | ProblemDetails + `traceId` |
| Not on app team (read of a specific incident) | 404 | ProblemDetails |
| Role OK but transition not allowed for that role | 403 | from the domain exception via `ProblemDetailsMiddleware` |

- `JwtBearerEvents.OnChallenge` / `OnForbidden` are routed through `ProblemDetailsMiddleware` (don't hand-build responses).
- Swagger: add `Bearer` security scheme (OAuth2 auth-code + PKCE against the API registration, scope `access_as_user`) so
  Swagger UI can authenticate in Development. **Regenerate `contracts/swagger.json`** (security scheme + 401/403 responses);
  CI fails on an unexpected diff.
- Reporters: DTO projections for comments **must filter `IsInternal`** in the query (not after load) — covered by tests.

## 11. Data model additions

`Domain/Apps/AppTeamMember`: `ApplicationId`, `UserId`, `TeamRole` (Member | Lead), `AddedAt` (DateTimeOffset UTC).
Unique index `(ApplicationId, UserId)`; index `(UserId)` for "my apps" lookups. `User`: `Id` (Guid), `EntraObjectId` (unique),
`DisplayName`, `Email`, `LastSeenAt`. Config classes in `Infrastructure/Persistence/Configurations/`; migration via
`dotnet ef migrations add AddUsersAndAppTeams …` — applied by the pipeline, never at startup. History rows store `UserId`
as actor (`IncidentHistory` stays append-only).

## 12. Worker

No inbound auth. The Worker never impersonates users: notifications and SLA escalations record the actor as a
fixed system actor (`Actor.System`). If Graph lookups (e.g. resolve escalation contact) are needed later, use
**managed identity / client credentials**, not user tokens, and add it with its own spec.

## 13. Security & operations checklist

- No secrets in appsettings; client secret/cert in Key Vault; prefer certificate or managed identity federated credentials.
- Rate-limit unauthenticated failures at the edge (APIM/WAF) — API only logs.
- Audit: every authorization **denial on a write** logs `{UserId} {Policy} {ApplicationId} {IncidentNumber}` at `Warning`.
- Add an OpenTelemetry span/tag for Redis team lookups.
- Nothing in Domain/Application references `ClaimsPrincipal`.

## 14. Testing (xUnit; naming `Method_Scenario_ExpectedResult`)

**Application.UnitTests** (NSubstitute, FluentAssertions)
- `GroupRoleClaimsTransformation`: maps each group to its role; union for multiple groups; unknown groups ignored; no groups → no roles;
  overage indicated → no roles + warning logged; idempotent on repeated calls.
- `AppTeamAuthorizationHandler`: member → succeeds; non-member → fails; Admin non-member → fails for incident actions;
  cache hit avoids reader call.
- Options validation: missing `TenantId`/`ClientId`/empty `GroupRoleMap` fails on start.

(If these classes live in `Api`, test them from the integration project or add an `Api.UnitTests` project via a spec update —
**do not** reference `IncidentHub.Api` from `Application.UnitTests` or break the dependency direction.)

**Domain.UnitTests**
- Every allowed **and** forbidden `TransitionTo` per role/actor against the workflow table (existing requirement; add `Actor` role cases).

**Api.IntegrationTests** (`[Trait("Category", "Integration")]`, `WebApplicationFactory` + Testcontainers; a `TestAuthHandler`
replaces JWT validation and lets tests set `oid`, `groups`/roles)
- 401 without token; 403 for authenticated user with no mapped role; 403 for wrong role per policy (table in §6).
- Team check: member vs non-member on write (403) and read (404).
- Reporter never receives internal comments via REST **and** SignalR; Responder does.
- SignalR: unauthenticated connection rejected; `access_token` query accepted on `/hubs/*` only; users join the right groups.
- Token tests that need real validation (expired, wrong audience/issuer) use a locally signed JWT with a test signing key
  wired through `JwtBearerOptions` in the factory — no calls to Entra in CI.
- ProblemDetails shape (`traceId`) on 401/403.

## 15. Implementation order (when approved)

1. Config + options validation (tenant check included; no new packages needed).
2. `User`, `AppTeamMember` entities, EF config, migration; `IAppTeamReader`.
3. JWT bearer setup, claims transformation, `ICurrentUser`, fallback policy; wire `UseAuthentication` **before** `UseAuthorization` in `Program.cs`.
4. `Policies` + `AppTeamRequirement`/handler; apply to controllers as they are added.
5. ProblemDetails integration for 401/403; Swagger security scheme; regenerate `contracts/swagger.json`.
6. SignalR auth + group joins.
7. Tests alongside each step; `dotnet format`; update CLAUDE.md only if a rule changes.

## 16. Resolved decisions

| Question | Decision |
|---|---|
| Admin global bypass for incident read/write? | **No.** Admin must be a team member; bypass only for application management and dashboard. |
| Apps open to all reporters? | **No.** Team membership is always required to report; no flag on `MonitoredApp`. |
| Graph group-overage handling? | **Deferred.** Fail closed + warning log; future spec if it occurs. |
| Tenancy? | **Single tenant, members only.** No B2B guest support. |
