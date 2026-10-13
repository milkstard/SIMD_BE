# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/06_01_duplicate-hints.md` (BE-06, part 1 of 1) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `903879f` |

### Progress
- [x] Docs/tracking: spec OQ 5-8 decided, deferred/completed items
- [x] Application: `ISimilarIncidentSearch`, `IncidentMessages`, `IncidentProjections`, `FindSimilarIncidents` query/validator/handler
- [x] Infrastructure: `SqlSimilarIncidentSearch`, DI
- [x] Api: `IncidentsController.Similar`
- [x] API-CONTRACT.md + `contracts/swagger.json` + CLAUDE.md Commands filter
- [x] Tests: validator, handler, search, endpoint, performance (50k rows)

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test --filter Category!=Performance` (Docker up): Domain 306, Application 82, Api.UnitTests 68, Api.IntegrationTests 217 passed, 0 failed (5 new unit tests, 30 new integration tests).
- `dotnet test --filter Category=Performance`: `Similar_50kSeededRows_P95Under300Ms` passed, p50 88.8 ms / p95 111.5 ms / max 185.0 ms over 100 calls against 50k seeded rows.
- `dotnet format --verify-no-changes`: clean. `contracts/swagger.json` regenerated (additive: `GET /api/v1/incidents/similar`).
- Note: Testcontainers on this machine needed `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine` (the default `docker_engine` pipe was absent).

### Blockers / open questions
None. Decided 2026-10-13: title < 3 chars -> `200 []`; policy `CanReportIncident`; FTS lag accepted and documented; perf test excluded via a documented `--filter`.

### Review
`/review-spec` on 2026-10-13 · change set: uncommitted working tree on `903879f` (06_01).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 673 passed / 0 failed / 1 performance test run separately (p95 107.6 ms); `dotnet format` clean; swagger matches the controller (first pass Incomplete: performance, format and swagger checks skipped; closed by one targeted rerun); 0 findings, 1 optional proposed test (tie-break order) |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 1 Low (reporter/assignee emails visible to team members incl. Reporters; contract-consistent) |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 4 Low |

Verdict: **Approved**.

Open non-blocking items: L1 the "opt-in" performance test also runs in a plain `dotnet test` (only `--filter Category!=Performance` excludes it) and leaves 50k rows that slow later full-text waits - reword the docs or gate it behind an env var; L2 `Handle_UnknownApplication` does not isolate the missing-application branch (stub `IsMemberAsync` to `true`); L3 `'Closed'` literal in the raw SQL (pass `nameof(IncidentStatus.Closed)` as a parameter); L4 a hint can include an incident closed between the search and the projection (negligible); proposed test `FindOpenAsync_EqualRank_NewestFirst`.

### Previous implementation (05_02)
Approved after review round 3 (QA 637/637, security 0 findings, code review 2 Low); committed as `7656473`. Open non-blocking items: L1 stock `RequestSizeLimit` filter may log a "could not apply limit" warning on requests with an `Idempotency-Key`; L2 bodies over 30 KB are buffered to a temp file; Teams/Applications creates without an `Idempotency-Key` still use Kestrel's 30 MB default.

## Log
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
