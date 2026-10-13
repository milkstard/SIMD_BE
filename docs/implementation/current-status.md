# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/09_02_post-commit-broadcasts.md` (BE-09, part 2 of 2) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `afcee75` |

### Progress
- [x] Docs/tracking: spec decisions (09_02 §8), deferred/completed items, API-CONTRACT §7
- [x] Application: `IPostCommitDispatcher`, `IDashboardSummaryReader` (+ null object), `IAppTeamReader.GetOwningTeamIdAsync`, `IncidentBroadcaster` + reader
- [x] Infrastructure: post-commit hook in `AppDbContext` (fresh scope, 2 s cap, logged failures)
- [x] Api: `HubPostCommitDispatcher`, `DashboardThrottle`, DI
- [x] Worker: no-op dispatcher
- [x] Tests written: `IncidentBroadcasterTests` (8), `DashboardThrottleTests` (7), integration `BroadcastTests` (11) and `PostCommitDispatchTests` (3)
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` clean.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 456, Application 204, Api.UnitTests 82 passed, 0 failed.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 499 passed, 0 failed (new: `BroadcastTests` 11, `PostCommitDispatchTests` 3). An earlier full run showed 16 failures in `AssignIncidentTests`, `CanWatchIncidentHandlerTests` and `CommentTests`; those classes pass alone and the full rerun was green, but I did not capture the cause (suspected load or overlapping runs).
- `contracts/swagger.json` not regenerated (no endpoint or DTO change). Not run: a manual two-host smoke test outside the test host.

### Decisions (2026-10-13)
Throttle per process; inline dispatch capped at 2 s; broadcast summaries carry the public `commentCount`; `AttachmentAdded` broadcasts nothing (files ride on `IncidentCreated`/`CommentAdded`).

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1241/1241 passed (integration 499, run twice), 0 proposed tests required, 1 Medium, 5 Low |
| Security | Passed | 0 Critical / 0 High / 1 Medium / 3 Low |
| Senior code review | Approved | 0 Critical / 0 High / 2 Medium / 4 Low |

Verdict: **Approved**. Open non-blocking items: QA M1 no test for an explicit transaction rollback (the hook runs after the real commit only because no handler opens a transaction), L1 2 s cap untested, L2 no test that the broadcast `commentCount` excludes internal comments, L3 no tests for `AttachmentAdded`/null reader, L4 sync `SaveChanges` blocks on the async dispatch; SEC-1 (Medium) `incident:{id}` watchers keep receiving after an incident moves to another team (extends the 09_01 stale-membership decision), SEC-2/QA L5 a timed-out dispatch is not cancelled (pass a `CancellationTokenSource(Timeout)` token), SEC-3 internal routing trusts the event flag instead of the stored row, SEC-4 transitive MessagePack; code review M1 `IAppTeamReader.GetOwningTeamIdAsync` has no caller, M2 one failing send in `HubPostCommitDispatcher` skips the remaining messages and the dashboard notify, L1 summary and owning team take two queries (spec says one), L2 the old team is not told that an incident left it, L3 timer and scope per leading edge while the reader is the null-object, L4 `AppDbContext` needs an `IPostCommitDispatcher` registration from the host. The cause of the 16 transient failures in one earlier full run was not found (not reproduced in two reruns).

### Previous implementation (08_03)
Approved (QA 1211/1211, security 2 Low, code review 5 Low, fixes applied; 1213 tests green); committed as `afcee75`.

### Previous implementation (08_02)
Approved (QA 1154/1154, security 3 Low, code review 1 Medium / 5 Low, all fixed afterwards; 1163 tests green); committed as `2a60af1`. Pending items: a manual smoke test with the real Worker process, and no Application-level `GetAttachmentDownloadHandlerTests`.

### Previous implementation (09_01)
Approved (QA 1011 passed, security 0 Critical/High, code review 4 Low); committed as `852e761`. Open non-blocking items are listed under "Review" below.

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
- 2026-10-13 · 09_02 · Reviewed · Approved — QA 1241/1241 (1 Medium, 5 Low), security 0 Critical/High (1 Medium, 3 Low), code review 2 Medium / 4 Low.
- 2026-10-13 · 09_02 · Done · Post-commit hook, broadcasts and dashboard throttle; integration 499 + unit tests green with Docker; review pending.
- 2026-10-13 · 09_02 · Blocked · Code and tests complete, unit tests green; integration run waits for Docker.
- 2026-10-13 · 09_02 · In progress · Started; post-commit hook, broadcasts and dashboard throttle.
- 2026-10-13 · 08_03 · Fixed · Review M1 (deterministic concurrency test) and L5; L1 was already satisfied (test added); 1213 tests green, format clean.
- 2026-10-13 · 08_03 · Reviewed · Approved — QA 1211/1211 (1 Medium, 3 Low), security 2 Low, code review 5 Low.
- 2026-10-13 · 08_03 · Done · Attachment linking on report and comments, real `attachments`, `AttachmentAdded`, orphan sweeper; full suite green with Docker (1211 tests); review pending.
- 2026-10-13 · 08_03 · In progress · Started; pulls in linking `attachmentIds`, real `attachments`, `AttachmentAdded` and the orphan sweeper from deferred items.
- 2026-10-13 · 08_02 · Fixed · Remaining review items SEC-1/2/3, L1, L2, L4, L5; 1163 tests green, format clean.
- 2026-10-13 · 08_02 · Fixed · Review M1 (`ScanAttempts`, fair claim order) and L3 (per-pass stuck-pending warning); 1161 tests green, format clean.
- 2026-10-13 · 08_02 · Reviewed · Approved — QA 1154/1154, security 3 Low, code review 1 Medium / 5 Low.
- 2026-10-13 · 08_02 · Done · Attachment upload/download endpoints, Azure Blob store (Azurite locally), stub scanner + Worker scan job, Redis lock; full suite green with Docker (1154 tests).
- 2026-10-13 · 08_02 · In progress · Started; pulls in the Attachment entity/table half of the entities item.
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
