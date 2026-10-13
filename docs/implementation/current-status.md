# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/07_02_incident-transition-and-assignment.md` (BE-07, part 2 of 3) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `38a3e72` |

### Progress
- [x] Docs/tracking: spec OQ decided, deferred/completed items
- [x] Application: `IncidentWriteGuard`, `LoadDetailAsync`, `TransitionIncident`, `AssignIncident`
- [x] Api: `IncidentsController.Transition/Assign`, `TransitionRequest`/`AssignRequest`
- [x] API-CONTRACT.md + `contracts/swagger.json` (additive: two POST operations, 453 insertions, 0 deletions)
- [x] Tests: validators, transition/assignment endpoints, outbox rows, concurrency rollback

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test --filter Category!=Performance` (Docker up): Domain 355, Application 129, Api.UnitTests 68, Api.IntegrationTests 315 passed, 0 failed (after the M1 fix).
- `dotnet format --verify-no-changes`: clean. `contracts/swagger.json` regenerated (additive).
- No Infrastructure change was needed: `AppDbContext.StageOutbox` already writes a row for every domain event; the endpoint tests prove the `IncidentTransitioned` / `IncidentAssigned` rows and the rollback (a concurrent save leaves no extra history or outbox row).
- Stale writes: the loaded `RowVersion` is compared with `If-Match` (409) and a writer that commits in between fails the `UPDATE` (`DbUpdateConcurrencyException`, 409); the concurrent test with one ETag gives exactly one 200 and one 409.
- Docker Desktop had to be started (not running at session start).

### Blockers / open questions
None. Decided 2026-10-13: non-member gets 404 (Admin included); assignee must be a member of the application's owning team; `teamId` may be any existing team; `duplicateOfId` must be a different incident the caller can see.

### Review
`/review-spec` on 2026-10-13 · change set: uncommitted working tree on `38a3e72` (07_02).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 865 passed / 0 failed (Domain 355, Application 129, Api.UnitTests 68, Api.IntegrationTests 313); 0 proposed tests; swagger regeneration and `dotnet format` not re-run by QA |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 1 Low (transition `note` stored unsanitized; only matters if the UI renders history notes as HTML) |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 5 Low |

Verdict: **Approved**.

Fixed after review: M1 (the duplicate-target check now runs after `TransitionTo` and before the save, so 403/409 win over 422; two new endpoint tests). Open non-blocking items: L1 the detail is read after the commit (a failed read turns a committed write into a 500; one detail projection would help, same as 07_01 L2); L2 `Users.AnyAsync` is redundant before `IsMemberAsync` (FK); L3 closed-incident 409 comes after the assignee/team lookups; L4 name the `(IncidentStatus)(-1)` sentinel; L5 a concurrent team delete would surface as 500 (teams are never deleted today). QA Lows: `allowedTransitions >= 0` assertion is vacuous, rollback test uses `DateTimeOffset.UtcNow`, the `DbUpdateConcurrencyException` to 409 path has no endpoint test, no TeamLead/Admin caller in the theory. Security Low: sanitize or tag-strip transition notes if history is rendered as HTML.

### Previous implementation (07_01)
Approved (QA 810/810 + plan test, security 3 Low, code review 2 Medium / 5 Low); committed as `38a3e72`. Open non-blocking items: M1 severity rank is written four times in `ListIncidentsHandler` (one shared expression); M2 keyset tie-break branches untested (seed identical `CreatedAt`/`UpdatedAt`); L1 `IncidentPermissions.AllowedTransitions` is a pass-through and the create mapper still calls the workflow directly; L2 detail takes three round trips; L3 `severity` and `-updatedAt` sorts are not plan-tested or indexed; L4 add a stop-word-only `q` case and note the 500-id cap in API-CONTRACT; L5 sort token parsing lives in the controller.

### Previous implementation (06_01)
Approved (QA 673/673, security 1 Low, code review 4 Low); committed as `a2bba9d`. Open non-blocking items: L1 the "opt-in" performance test also runs in a plain `dotnet test`; L2 `Handle_UnknownApplication` does not isolate the missing-application branch; L3 `'Closed'` literal in the raw SQL; L4 a hint can include an incident closed between search and projection.

### Previous implementation (05_02)
Approved after review round 3 (QA 637/637, security 0 findings, code review 2 Low); committed as `7656473`. Open non-blocking items: L1 stock `RequestSizeLimit` filter may log a "could not apply limit" warning on requests with an `Idempotency-Key`; L2 bodies over 30 KB are buffered to a temp file; Teams/Applications creates without an `Idempotency-Key` still use Kestrel's 30 MB default.

## Log
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
