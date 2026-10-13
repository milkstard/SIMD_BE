# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/08_02_attachment-storage-and-download.md` (BE-08, part 2 of 3) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-13 |
| Last updated | 2026-10-13 |
| Branch / commit | main / `852e761` |

### Progress
- [x] Docs/tracking: spec decisions (08_02 §8), deferred/completed items
- [x] Domain: `ScanStatus`, `Attachment`, `AttachmentRules`
- [x] Application: `IFileStore`, `IAttachmentScanner`, `IDistributedLock`, `UploadAttachment`, `GetAttachmentDownload`, exceptions
- [x] Infrastructure: `AttachmentConfiguration` + `AddAttachments` migration, `AzureBlobFileStore`, container initializer, stub scanner, `RedisDistributedLock`, `AttachmentScanRunner`
- [x] Api: `AttachmentsController`, multipart reader, problem mappings, swagger
- [x] Worker: `AttachmentScanJob`
- [x] API-CONTRACT.md, DATABASE.md, CLAUDE.md config line, `contracts/swagger.json`
- [x] Tests (unit + integration with Azurite)

### Verification
- `dotnet build` (whole solution): 0 warnings, 0 errors.
- `dotnet test --filter Category!=Performance` with Docker: Domain 438, Application 189, Api.UnitTests 75, Api.IntegrationTests 452 passed, 0 failed (52 are new attachment tests: upload, download, scan job, storage contract against Azurite, Redis lock).
- `dotnet format --verify-no-changes` clean; `contracts/swagger.json` regenerated (new `/attachments` operations, multipart body).
- Not run: the manual smoke test with the real Worker process (the scan job is covered through `AttachmentScanRunner`).
- Deviations from the spec: non-multipart body is `400` (no `[Consumes]`, which would give `415`); `AttachmentScanRunner`, scanner and lock are registered by `AddInfrastructure` (the tests resolve them from the API host) instead of a separate `AddAttachmentScanning`; blob retries use the Azure SDK policy, not Polly.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1154/1154 passed (Domain 438, Application 189, Api.UnitTests 75, integration 452), 0 proposed tests required, 3 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 3 Low |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 5 Low |

Verdict: **Approved**. Fixed after the review: M1 (new `Attachments.ScanAttempts`; the claim orders by attempts then age, so unreadable files sink behind new uploads; test `Scan_RowsThatKeepFailing_DoNotStarveNewerUploads`, which fails without the fix) and L3 (once per pass the oldest `Pending` row is checked and a warning logged when it is over 1 hour old, whatever the cause; two tests). The `AddAttachments` migration was regenerated (never applied or committed). Full suite re-run green (1161 tests), build 0 warnings, format clean, swagger unchanged. Also fixed afterwards: SEC-1 (SAS is HTTPS-only except on the plain-HTTP emulator), SEC-2 (format/bidi/zero-width characters and line separators stripped from file names), SEC-3 (the scan pass stops starting files after a 30 s budget, so it ends before the 60 s lock expires), L1 (a failing container creation at start-up is logged, the host keeps running), L2 (`InvariantCulture` in the blob path), L4 (the `Location` 409-until-Clean behaviour is documented in API-CONTRACT), L5 (a failed lock release is logged as a warning, not a failed pass). The 1-hour warning test and the 'no FileResult' guard (302 asserted by the endpoint tests) were already covered; no Application-level `GetAttachmentDownloadHandlerTests` because the handler is an EF projection (covered by the endpoint tests). New tests: bidi/zero-width file names, spent scan budget. Full suite 1163 green, build 0 warnings, format clean. Pre-existing dependency advisories (MessagePack, SSH.NET) are tracked under 08_01 SEC-2.

### Blockers / open questions
None. Decided 2026-10-13: `IncidentId` nullable + `CommentId` (uploader-only until linked; internal-comment attachments 404 for non-responders); allowed types png/jpeg/gif/webp/txt/log/json; 413 size / 422 type; stuck `Pending` only logs; container created at startup; build `IDistributedLock` now; blob resilience via the Azure SDK retry policy instead of Polly (a non-seekable upload stream cannot be replayed).

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
