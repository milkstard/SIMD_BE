# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/07_01_incident-list-and-detail.md` (BE-07, part 1 of 3) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `b48d451` |

### Progress
- [x] Docs/tracking: spec OQ decided, deferred/completed items
- [x] Domain: value-based `IncidentWorkflow.AllowedTransitions`
- [x] Application: `IncidentPermissions`, `IIncidentTextSearch`, `IncidentCursor`, `ListIncidents`, `GetIncident`, detail projection
- [x] Infrastructure: `SqlIncidentTextSearch`, DI
- [x] Api: `IncidentsController.List/Get`, `ListIncidentsRequest`
- [x] API-CONTRACT.md + `contracts/swagger.json` (additive: `GET /incidents`, `GET /incidents/{number}`)
- [x] Tests: domain, validator, permissions, cursor, list/get endpoints, text search, query-plan (opt-in)

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test --filter Category!=Performance` (Docker up): Domain 355, Application 119, Api.UnitTests 68, Api.IntegrationTests 268 passed, 0 failed.
- `dotnet test --filter Category=Performance` (`ListIncidentsQueryPlanTests`, 50k rows): status+severity -> `IX_Incidents_Status_Severity` seek; application + date range -> `IX_Incidents_Application_CreatedAt` seek; default list -> `IX_Incidents_Application_CreatedAt` scan; default list p95 48.4 ms.
- `dotnet format --verify-no-changes`: clean. `contracts/swagger.json` regenerated (additive).
- Finding: a parameterised `status`/`severity` list (OPENJSON) made the optimizer scan the clustered index; the lists are inlined with `EF.Constant` (tiny enums) so the selective seek is chosen.
- Docker Desktop had to be started (not running at session start).

### Blockers / open questions
None. Decided 2026-10-13: own teams only (Admin included); Reporter sees all of the team's incidents; `q` = FREETEXTTABLE top 500; query-plan test asserts indexes for selective filters only.

### Review
`/review-spec` on 2026-10-13 · change set: uncommitted working tree on `b48d451` (07_01).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 810 passed / 0 failed + plan test passed (default list p95 67.8 ms); `dotnet format` clean; swagger diff additive (regeneration not re-run by QA); 0 proposed tests |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 3 Low (transitive MessagePack advisory, unmetered full-text/`includeTotal` cost, 30 s cached membership on detail) |
| Senior code review | Approved | 0 Critical / 0 High / 2 Medium / 5 Low |

Verdict: **Approved**.

Open non-blocking items: M1 severity rank is written four times in `ListIncidentsHandler` (one shared expression); M2 keyset tie-break branches untested (seed identical `CreatedAt`/`UpdatedAt`); L1 `IncidentPermissions.AllowedTransitions` is a pass-through and the create mapper still calls the workflow directly; L2 detail takes three round trips (one detail projection would also tie ETag and body to the same read); L3 `severity` and `-updatedAt` sorts are not plan-tested or indexed; L4 add a stop-word-only `q` case ("the") to the text-search test and note the 500-id cap in API-CONTRACT; L5 sort token parsing lives in the controller (sort is a plain string in swagger).

### Previous implementation (06_01)
Approved (QA 673/673, security 1 Low, code review 4 Low); committed as `a2bba9d`. Open non-blocking items: L1 the "opt-in" performance test also runs in a plain `dotnet test`; L2 `Handle_UnknownApplication` does not isolate the missing-application branch; L3 `'Closed'` literal in the raw SQL; L4 a hint can include an incident closed between search and projection.

### Previous implementation (05_02)
Approved after review round 3 (QA 637/637, security 0 findings, code review 2 Low); committed as `7656473`. Open non-blocking items: L1 stock `RequestSizeLimit` filter may log a "could not apply limit" warning on requests with an `Idempotency-Key`; L2 bodies over 30 KB are buffered to a temp file; Teams/Applications creates without an `Idempotency-Key` still use Kestrel's 30 MB default.

## Log
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
