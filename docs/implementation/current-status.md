# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/09_01_hub-groups-and-access.md` (BE-09, part 1 of 2) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `24d65aa` (07_03 and 08_01 work is still uncommitted in the tree) |

### Progress
- [x] Docs/tracking: spec decisions (09_01 §8, `GetOwningTeamIdAsync` moved to 09_02)
- [x] Application: `IAppTeamReader.GetTeamIdsForUserAsync`, `CanWatchIncident` query/validator/handler, hub payload DTOs (`AppStatusItemDto`, `DashboardSummaryDto`, `NotificationDto`, `CommentAddedEvent`) and `AppStatus`/`NotificationType` enums
- [x] Infrastructure: `RedisAppTeamReader.GetTeamIdsForUserAsync`
- [x] Api: `IIncidentClient` (6 methods), `HubGroups`, `IncidentsHub` rewrite, hub JSON protocol with string enums
- [x] API-CONTRACT.md §7
- [x] Tests written: `IncidentsHubTests` (replaces `IncidentsHubAuthTests`), probe rewrite, `CanWatchIncidentHandlerTests`, `GetTeamIdsForUserAsync_ReturnsOnlyOwnTeams`, validator and client-contract unit tests
- [x] Integration run with Docker

### Verification
- `dotnet build` (whole solution): 0 warnings, 0 errors.
- Unit tests (`Category!=Integration`): Domain 369, Application 169, Api.UnitTests 75 passed, 0 failed.
- Full suite with Docker (`Category!=Performance`): 397 of 399 integration tests pass, including all new hub tests (`IncidentsHubTests`, `CanWatchIncidentHandlerTests`, `GetTeamIdsForUserAsync_ReturnsOnlyOwnTeams`). Two failures belong to the uncommitted 08_01 work, not 09_01: `CommentTests.Add_BodyOverRequestCap_Returns413` (gets 422) and `FindSimilarIncidentsHandlerTests.Handle_Matches_ReturnsSummariesInRankOrder` (NullReferenceException in `IncidentWorkflow.IsResponder`: the test's substitute `ICurrentUser` returns null `Roles` since the handler now checks the responder role).
- `dotnet format --verify-no-changes` reports CHARSET/IDE0161 only in `Migrations/20261013081549_AddComments.cs` (08_01 work, not this spec); the 09_01 files are clean.

### Blockers / open questions
None. Decided 2026-10-13: `dashboard:{teamId}` + `team:{teamId}` groups; stale membership accepted until reconnect; responder groups follow the token role; `GetOwningTeamIdAsync` moves to 09_02.

### Previous implementation (08_01)
Done, review pending (`/review-spec`). `GET/POST /incidents/{id}/comments`, `GET /incidents/{id}/history`, `Comment` entity + `AddComments` migration, real `commentCount`.
- Verification: `dotnet build` 0 warnings; `dotnet test --filter Category!=Performance` with Docker: Domain 369, Application 169, Api.UnitTests 75, Api.IntegrationTests 399 passed, 0 failed; opt-in plan tests pass (`ListIncidentsQueryPlanTests`, new `CommentsQueryPlanTests` seeks `IX_Comments_Incident_CreatedAt`); `dotnet format --verify-no-changes` clean; `contracts/swagger.json` regenerated (additive).
- Decisions: see spec 08_01 §8 (history row per comment with `InternalComment` hidden from non-responders, names in history values, comments on closed incidents, no `UpdatedAt`/`ETag` bump, per-caller `commentCount`, 128 KiB body cap).
- Touched shared files: `FindSimilarIncidentsHandlerTests` now stubs `ICurrentUser.ToActor()` (the projection needs the caller's role for `commentCount`); `AuthProbeController` uses the new `CommentDto` shape.

#### Review (08_01)
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed (second run; the first was Incomplete, Docker went down) | 1012/1012 passed, 2/2 plan tests, format clean, 0 proposed tests required |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 7 Low |

Verdict: **Approved**. Fixed after the review: SEC-1 (the body length is checked again after sanitizing, `422` on `body`, new test `Add_BodyLongerThanLimitOnceSanitized_Returns422`) and the stale 64 KiB text in the spec; full suite re-run green (1013 tests, build 0 warnings, format clean).
Open non-blocking items: SEC-2 vulnerable transitive packages (MessagePack in Api, SSH.NET in tests; not from 08_01); L2 cursor decoding lives in the list handlers instead of the validators; L3 the internal-comment rule is checked in the handler and the domain; L4 keyset paging is duplicated in `ListCommentsHandler`/`ListHistoryHandler`; L5 fully qualified `IncidentNumber` in `HistoryValueResolver`; L6 the `Comment` entry in `HistoryFieldNames` is a no-op; L7 `AddComment` loads the whole incident. QA Lows: no test for `ApplicationId` name resolution in history, `UpdatedAt` is asserted unchanged only in the domain test, the POST response author comes from the token while GET joins `Users`.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1011 passed; 1 failure in 08_01 code (`FindSimilarIncidentsHandlerTests`, fixed by the other session's edit, not reproduced on rerun); 1 Medium (F1), 3 Low |
| Security | Passed | 0 Critical, 0 High, 1 Medium (SEC-1), 3 Low |
| Senior code review | Approved | 0 Critical, 0 High, 0 Medium, 4 Low |

Open non-blocking items: F1 negative group assertions can pass before team groups are joined (`ConnectAsync` waits only for `user:`); F3 no test asserts string enums on the hub wire; SEC-1 hub calls unthrottled, `JoinDashboard`/`LeaveDashboard` hit SQL per call (cap watches per connection, reuse connect-time team ids); SEC-2 responder groups follow the token role (decision 4), SEC-3 stale membership until reconnect (decision 3), both to revisit in 09_02; SEC-4 pin a patched transitive `MessagePack`; L1 `CanWatchIncidentValidator` duplicates the hub's empty-id guard; L2 sequential group adds; L3 hub wrong-scope has no hub-level test (the scope policy is covered by `JwtValidationTests`); L4 reword the "malformed" comment in `WatchIncident`. `dotnet format` was not run by the reviewers; it only flags the 08_01 migration.

### Previous implementation (07_03)
Done (906 tests green, format clean); review pending. Edit endpoint `PATCH /incidents/{id}`.

### Previous implementation (07_02)
Approved (QA 865/865, security 1 Low, code review 1 Medium fixed / 5 Low); committed as `24d65aa`. Open non-blocking items: L1 the detail is read after the commit (one projection would help); L2 `Users.AnyAsync` is redundant before `IsMemberAsync`; L3 closed-incident 409 comes after the assignee/team lookups; L4 name the `(IncidentStatus)(-1)` sentinel; L5 a concurrent team delete would surface as 500. QA Lows: vacuous `allowedTransitions >= 0` assertion, rollback test uses `DateTimeOffset.UtcNow`, the `DbUpdateConcurrencyException` to 409 path has no endpoint test. Security Low: transition `note` is stored unsanitized.

### Previous implementation (07_01)
Approved (QA 810/810 + plan test, security 3 Low, code review 2 Medium / 5 Low); committed as `38a3e72`. Open non-blocking items: M1 severity rank is written four times in `ListIncidentsHandler` (one shared expression); M2 keyset tie-break branches untested (seed identical `CreatedAt`/`UpdatedAt`); L1 `IncidentPermissions.AllowedTransitions` is a pass-through and the create mapper still calls the workflow directly; L2 detail takes three round trips; L3 `severity` and `-updatedAt` sorts are not plan-tested or indexed; L4 add a stop-word-only `q` case and note the 500-id cap in API-CONTRACT; L5 sort token parsing lives in the controller.

### Previous implementation (06_01)
Approved (QA 673/673, security 1 Low, code review 4 Low); committed as `a2bba9d`. Open non-blocking items: L1 the "opt-in" performance test also runs in a plain `dotnet test`; L2 `Handle_UnknownApplication` does not isolate the missing-application branch; L3 `'Closed'` literal in the raw SQL; L4 a hint can include an incident closed between search and projection.

### Previous implementation (05_02)
Approved after review round 3 (QA 637/637, security 0 findings, code review 2 Low); committed as `7656473`. Open non-blocking items: L1 stock `RequestSizeLimit` filter may log a "could not apply limit" warning on requests with an `Idempotency-Key`; L2 bodies over 30 KB are buffered to a temp file; Teams/Applications creates without an `Idempotency-Key` still use Kestrel's 30 MB default.

## Log
- 2026-10-13 · 08_01 · Reviewed · Approved — QA 1012/1012 (+1 test after the fix), security 2 Low, code review 7 Low; SEC-1 fixed, 1013 tests green, format clean.
- 2026-10-13 · 09_01 · Reviewed · Approved — QA 1011 passed, security 0 Critical/High (1 Medium, 3 Low), code review 4 Low.
- 2026-10-13 · 08_01 · Done · Comments and history endpoints, `Comments` table, real `commentCount`; full suite green with Docker (1012 tests), plan test seeks the comments index; review pending.
- 2026-10-13 · 08_01 · In progress · Started; pulls in the Comment half of the entities item and real `commentCount` from deferred items.
- 2026-10-13 · 09_01 · Done · Hub contract, groups and watch checks; 397/399 integration tests green (2 failures in 08_01 code); review pending.
- 2026-10-13 · 09_01 · Blocked · Code and tests complete, unit tests green; integration run waits for Docker.
- 2026-10-13 · 09_01 · In progress · Started; hub contract, groups and access checks (no broadcasts yet).
- 2026-10-13 · 07_03 · Reviewed · Approved — QA 906/906, security 3 Low, code review 3 Low.
- 2026-10-13 · 07_03 · Done · `PATCH /incidents/{id}` with If-Match, role split (reporter: title/description/steps), severity reason + SLA restart, application move, duplicate 409; full suite green with Docker (906 tests); review pending.
- 2026-10-13 · 07_03 · In progress · Started; finishes the PATCH half of the team-membership check from deferred items.
- 2026-10-13 · 07_02 · Fixed · Review M1: duplicate-target check runs after the domain validates the move (403/409 before 422); 867 tests green, format clean.
- 2026-10-13 · 07_02 · Reviewed · Approved — QA 865/865, security 1 Low, code review 1 Medium / 5 Low.
- 2026-10-13 · 07_02 · Done · `POST /incidents/{id}/transitions` and `/assignment` with If-Match, 404 for non-members, outbox rows and refreshed detail; full suite green with Docker (865 tests); review pending.
- 2026-10-13 · 07_02 · In progress · Started; pulls in the write half (transition, assignment) of the team-membership check from deferred items.
- 2026-10-13 · 07_01 · Reviewed · Approved — QA 810/810 + plan test, security 3 Low, code review 2 Medium / 5 Low.
- 2026-10-13 · 07_01 · Done · `GET /incidents` (filters, keyset paging, 4 sorts, FREETEXTTABLE `q`) and `GET /incidents/{number}`; visibility scoped to the caller's teams; full suite green with Docker (810 tests) + plan test; review pending.
- 2026-10-13 · 07_01 · In progress · Started; pulls in the read half of the team-membership check and the index-backed list queries from deferred items.
- 2026-10-13 · 06_01 · Reviewed · Approved — QA 673/673 + perf p95 107.6 ms, security 1 Low, code review 4 Low.
- 2026-10-13 · 06_01 · Done · `GET /incidents/similar` (FREETEXTTABLE port, summary projection, 50k-row perf test p95 111.5 ms); full suite green with Docker (673 tests); review pending.
- 2026-10-13 · 06_01 · Blocked · Integration run waited for a responding Docker engine.
- 2026-10-13 · 06_01 · In progress · Started; pulls in the full-text similar-incidents query and the 50k-row seed from deferred items.
- 2026-10-12 · 05_02 · Reviewed · Approved (round 3) — QA 637/637, security 0 findings, code review 2 Low; size-limit bypass with Idempotency-Key closed.
- 2026-10-12 · 05_02 · Reviewed · Approved (round 2) — QA 632/632, security 2 Low, code review 1 Medium (size limit likely bypassed when an Idempotency-Key is sent).
- 2026-10-12 · 05_02 · Fixed · Review follow-ups: no post-commit read (M1), idempotency body-size cap + streamed hash + 413 mapping, fail-loud hash lookup; 632 tests green.
- 2026-10-12 · 05_02 · Reviewed · Approved — QA 629/629, security 2 Low, code review 1 Medium (post-commit detail read can turn a stored incident into a 500).
- 2026-10-12 · 05_02 · Done · ReportIncident use case, sanitizer, idempotency body hash, duplicate 409 with existing number, `POST /api/v1/incidents`; full suite green with Docker (629 tests).
- 2026-10-12 · 05_02 · In progress · Started; pulls in setting SLA due times at creation and the BE-05 half of the team-membership check from deferred items.
- 2026-10-12 · 05_01 · Reviewed · Approved — QA 561/561, security 2 Low, code review 1 Medium; follow-ups fixed, 571 tests green; committed as `057a417`.
- 2026-10-11 · 04_02 · Reviewed · Approved — QA 525/525, security 4 Low, code review 1 Medium (migration half-applies if Full-Text is missing).
- 2026-10-11 · 04_02 · Done · Persistence, `AddIncidents` migration (with full-text), FTS image and tests; full suite green with Docker.
- 2026-10-11 · 04_02 · Blocked · Code complete; integration run waited for Docker.
- 2026-10-11 · 04_02 · In progress · Started; nothing is deferred to BE-04.
- 2026-10-11 · 04_01 · Done · Incident aggregate, workflow and error mappings implemented (290 domain, 41 API, 53 application unit tests passing).
