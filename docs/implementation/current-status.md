# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/05_02_report-incident-endpoint.md` (BE-05, part 2 of 2) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-12 |
| Last updated | 2026-10-12 |
| Branch / commit | main / `7656473` |

### Progress
- [x] Docs/tracking: spec status + decided open questions, deferred/completed items
- [x] Application: `IHtmlSanitizer`, idempotency store contract (`RequestHash`), `DuplicateIncidentException` number, DTOs, `IncidentQueries`, `ReportIncident` command/validator/handler
- [x] Infrastructure: `HtmlSanitizerAdapter`, `RedisIdempotencyStore` hash, DI
- [x] Api: `ReportIncidentRequest`, `IncidentsController`, `IdempotencyFilter` body hash, `ProblemDetailsMiddleware` `existingIncidentNumber`
- [x] API-CONTRACT.md + `contracts/swagger.json`
- [x] Tests: validator, idempotency filter, middleware, sanitizer, handler (integration), endpoint (integration)

### Verification
- `dotnet build` (whole solution, warnings as errors): 0 warnings, 0 errors.
- `dotnet test` (all, Docker up): Domain 306, Application 76, Api.UnitTests 61, Api.IntegrationTests 186 passed, 0 failed (34 new integration tests in `Incidents/`; 17 new unit tests).
- `dotnet format --verify-no-changes`: clean. `contracts/swagger.json` regenerated (additive: `POST /api/v1/incidents` + DTO schemas).

### Blockers / open questions
None. Decided 2026-10-12: unknown enum name stays 400; handler tests live in Api.IntegrationTests; `canEdit`/`canAssign` as proposed (OQ 7); rate limiting deferred (OQ 9).

### Review
`/review-spec` on 2026-10-12 · change set: `7656473` (05_02).

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 629 passed / 0 failed / 0 not run; 12/12 AC covered (2 partial: stepsToReproduce sanitize test, format/swagger checked by orchestrator); 0 Critical/High/Medium, 3 Low; proposed tests 1 |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low (plus pre-existing MessagePack 2.5.108 and test-only SSH.NET advisories, not introduced by 05_02) |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 4 Low |

Verdict: **Approved**.

Follow-up (2026-10-12, after review): code review M1 fixed (detail DTO built from in-memory values, no query after the commit); idempotency body hashed as a stream with `[RequestSizeLimit(256 KiB)]` on the action and `BadHttpRequestException` mapped to its own status (413) in the middleware; missing hash now throws instead of falling back to empty; redundant `using` removed. `stepsToReproduce` sanitizing was already asserted in `Report_ScriptInDescription_StoredSanitized`. Rebuilt, `dotnet format` clean, swagger unchanged, full suite: Domain 306, Application 76, Api.UnitTests 64, Api.IntegrationTests 186 passed, 0 failed.

Still open (non-blocking, left as is): replay omits `ETag`; sanitizer default allow-list may allow remote images; legacy idempotency entries read as in-progress until TTL; controller maps a missing enum to `-1`.

Review round 2 (2026-10-12, after the follow-up above):

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 632 passed / 0 failed / 0 not run (first attempt Incomplete: Docker daemon down; rerun with Docker up); `dotnet format` clean; 0 new findings |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 3 Low |

Verdict: **Approved**. Open non-blocking items from round 2: M1 `[RequestSizeLimit]` is likely bypassed when an `Idempotency-Key` is sent, because `IdempotencyFilter` (resource filter, Order 0) buffers and hashes the body before the size-limit filter (Order 900) runs, and no test can cover it under TestServer (fix: `EnableBuffering(bufferThreshold, bufferLimit: 256 KiB)` or set `IHttpMaxRequestBodySizeFeature` in the filter); spec OQ 10 and API-CONTRACT §6 still say 413 is for files only; optional 4xx range check on `BadHttpRequestException.StatusCode`; hash is over raw bytes (document "byte-identical body").

Follow-up 2 (2026-10-12, after round 2): `IdempotencyFilter` now enforces the endpoint's `[RequestSizeLimit]` (or a 1 MiB default) itself before reading the body — sets `IHttpMaxRequestBodySizeFeature` and throws `BadHttpRequestException(413)` when `Content-Length` is over the limit — so the cap also holds when an `Idempotency-Key` is sent (and now covers Teams/Applications creates); `ProblemDetailsMiddleware` only passes 4xx statuses through; `413` added to the action's responses and to API-CONTRACT §1/§6 and spec OQ 10 (byte-identical body wording included). New tests: 4 filter unit tests, `Report_BodyOverSizeLimitWithIdempotencyKey_Returns413AndCreatesNothing`. Full suite: Domain 306, Application 76, Api.UnitTests 68, Api.IntegrationTests 187 passed, 0 failed; `dotnet format` clean; swagger regenerated.

Review round 3 (2026-10-12, after follow-up 2):

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 637 passed / 0 failed / 0 not run (two earlier attempts Incomplete: Docker daemon down; rerun with Docker up); `dotnet format` clean; swagger and API-CONTRACT consistent; 0 findings |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 0 Low (size-limit bypass closed) |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 2 Low |

Verdict: **Approved**. Open non-blocking items: L1 the stock `RequestSizeLimit` filter probably logs a "could not apply limit" warning on requests that carry an `Idempotency-Key` (limit still enforced by `IdempotencyFilter`); L2 bodies over 30 KB are buffered to a temp file (`EnableBuffering(bufferThreshold: limit)` would keep them in memory); Teams/Applications creates without an `Idempotency-Key` still use Kestrel's 30 MB default (unchanged, Admin only).

## Log
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
