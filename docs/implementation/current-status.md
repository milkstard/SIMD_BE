# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/10_03_email-triggers.md` (BE-10, part 3 of 3) |
| State | Done <!-- Planned · In progress · Blocked · Done --> |
| Started | 2026-10-14 |
| Last updated | 2026-10-14 |
| Branch / commit | main / `15a8a4f` (10_03 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (10_03 �9), `DATABASE.md` (`Error` marker), deferred/completed items
- [x] Application: `IEmailTemplateRenderer` + `RenderedEmail`, `EmailContentBuilder`, `IIncidentEmailReader`/`IncidentEmailReader`, `EmailDelivery`, `IncidentReported`/`IncidentAssigned`/`IncidentResolved` email handlers
- [x] Infrastructure: renderer implements the port, handlers + reader registered keyed by event type in `AddNotificationSenders`; SEC-A (`GetKeyedServices` per message type) and SEC-B (`NoHandlerNote`) in `OutboxDispatchRunner`/`OutboxMessage`
- [x] Worker: nothing beyond DI
- [x] Tests written: `EmailContentBuilderTests`, three handler test classes, `EmailContentRenderingTests`, `NotificationLayeringTests` (Api + Worker), `EmailDispatchTests` (integration, Mailpit), runner tests moved to keyed handlers (+ broken-handler-of-another-type case, no-handler marker)
- [x] Build, format, unit tests
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` clean.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 456, Application 259, Worker 4, Api.UnitTests 152 passed, 0 failed.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 535/535 passed (new `EmailDispatchTests` 9 with Mailpit, runner tests updated for keyed handlers).
- `contracts/swagger.json` unchanged (no endpoint or DTO change).

### Decisions (2026-10-14)
See spec 10_03 �9: open questions 1-5 accepted as proposed; SEC-A and SEC-B from the 10_02 review done here; the renderer port lives in Application.

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1406/1406 passed (integration 535 incl. Mailpit), 0 proposed tests required, 5 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 3 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 5 Low |

Verdict: **Approved**. Open non-blocking items: SEC-1 CR/LF in an incident title can forge lines in the plain-text body (collapse line breaks in the title fact); SEC-2 the Assigned mail goes to the event's assignee without checking the incident's current assignee (add `AssigneeId` to `IncidentEmailFacts` and skip when it differs); SEC-3 `LoggingEmailSender` logs the full subject (10_02 SEC-5); code review L1 the Resolved mail shows the send-time `Status`, which can contradict "Resolved" after a delayed retry, L2 `EmailDelivery` uses `Single` on the senders so a missing sender is retried 8 times (resolve once or throw `NonRetryableOutboxException`), L3 `EmailContent.Headline` always equals `Subject` (YAGNI), L4 keyed handlers use `AddKeyedScoped` instead of an idempotent add, L5 `MarkProcessed` leaves an earlier `Error` and the field now means both "last failure" and "skipped"; QA Lows: the SMTP-outage test advances the clock a fixed 2 min (use `NextAttemptAt`), no integration test that a skipped message does not block the next, no explicit team-only assignment unit test.

## Previous implementation (10_02)

### Progress
- [x] Docs/tracking: spec decisions (10_02 �9), `CLAUDE.md` Mailpit/config notes, 10_01 HandlerTimeout 60 s
- [x] Packages: `MailKit` 4.18.1, `Microsoft.Extensions.Resilience` 8.10.0, `System.Security.Cryptography.Pkcs` pinned to 10.0.13
- [x] Application: `INotificationSender`, `NotificationChannel`, `Notification`, `NotificationDeliveryException`
- [x] Infrastructure: `EmailOptions` + validator, `ISmtpTransport`/`MailKitSmtpTransport`, `EmailSender`, `LoggingEmailSender`, `EmailTemplateRenderer`, resilience options + pipeline, `AddNotificationSenders`
- [x] Worker: registration + `appsettings.json`; `docker-compose.yml` Mailpit
- [x] Tests written (unit, layering, Mailpit integration)
- [x] Build, format, unit tests
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` clean.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 456, Application 230, Worker 3, Api.UnitTests 142 passed, 0 failed (new: Notification 5, options validator, renderer, `EmailSender` mapping and SEC-E, resilience pipeline with `FakeTimeProvider`, DI/start-up validation, layering).
- `dotnet list package --vulnerable --include-transitive`: MimeKit 4.18.1 pulls `System.Security.Cryptography.Pkcs` 10.0.0 (High); pinned to 10.0.13 in Infrastructure, Infrastructure and Worker now report none.
- Integration tests with Docker: `EmailSenderSmtpTests` (Mailpit, 2) + outbox suite (45) = 47/47 passed. The first attempt errored at fixture start (Docker had dropped). The rest of the suite was not rerun (no API, schema or shared-code change).
- `contracts/swagger.json` unchanged (no endpoint or DTO change).

### Decisions (2026-10-14)
Sender registered in the Worker host only; `Outbox:Dispatcher:HandlerTimeout` default raised 30 s → 60 s so Polly's 45 s total fits; SEC-E (no full address or SMTP reply text in errors) done here; SEC-A and SEC-B from the 10_01 review move to 10_03; SEC-4 stays a separate chore.

### Blockers / open questions
None. Carried to 10_03: SEC-A (resolve handlers per event type), SEC-B (marker for rows without a handler).

### Review fixes (2026-10-14)
SEC-1 `UseStartTls` replaced by `Tls` (`None`/`StartTls`/`SslOnConnect`), the validator refuses `None` when a `Username` is set; SEC-2 generic failures carry no inner exception; code review L1 post-send disconnect bounded (5 s token, 15 s SMTP I/O timeout). Spec 10_02 �9 item 10. New tests: 4 validator cases, `Send_UnexpectedFailure_ErrorTextCarriesNoServerReplyOrAddress`.
- `dotnet build` 0 warnings; `dotnet format --verify-no-changes` clean. Unit tests: Domain 456, Application 230, Worker 3, Api.UnitTests 148 passed.
- Still open: SEC-3, SEC-4, SEC-5, QA L2, code review L2-L5.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1356/1356 passed (integration 525 incl. Mailpit), 0 proposed tests required, 0 Medium, 2 Low |
| Security | Passed | 0 Critical / 0 High / 1 Medium / 4 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 5 Low |

Verdict: **Approved**. Open non-blocking items: SEC-1 (Medium) nothing forces StartTLS when `Username` is set and implicit TLS (port 465) is unsupported (validator rule + `SecureSocketOptions` choice); SEC-2/QA L1 the inner exception of generic SMTP failures reaches the logs (drop it in `Map`'s default arm); SEC-3 auth replies (530/534/535/538) are retried and can lock the SMTP account; SEC-4 Mailpit ports bind on all interfaces (use `127.0.0.1:`); SEC-5 `LoggingEmailSender` logs the full subject; QA L2 an open circuit consumes dispatcher attempts (OQ 3, per spec); code review L1 `DisconnectAsync(.., CancellationToken.None)` is unbounded by the pipeline (set `client.Timeout` or skip the disconnect), L2 permanent failures count as breaker successes (add a comment), L3 `TotalTimeout < HandlerTimeout` is documented, not validated, L4 `Render` signature differs from the spec (link built from `PublicBaseUrl`), L5 resolve senders by `Channel` in 10_03. Untested: From/recipient-name CR/LF stripping, a non-command exception text. Carried to 10_03: SEC-A, SEC-B.

## Previous implementation (10_01)

State: Done, Approved (review round 2). Uncommitted.

| Field | Value |
|---|---|
| Spec | `docs/specs/10_01_outbox-dispatcher.md` (BE-10, part 1 of 3) |
| State | Done |

### Progress (10_01)
- [x] Docs/tracking: spec decisions (10_01 §9), `DATABASE.md`, `CLAUDE.md` layout line
- [x] Application: `IOutboxMessageHandler`, `NonRetryableOutboxException`, `OutboxDispatcherOptions` (+ validator), `OutboxBackoff`
- [x] Infrastructure: `OutboxMessage.NextAttemptAt` + `MarkProcessed`/`MarkFailed`/`Park`, configuration/index, migration `AddOutboxNextAttemptAt`, `OutboxEventTypes`, `IOutboxDispatchRunner` + `OutboxDispatchRunner`, `AddOutbox()`
- [x] Worker: `OutboxDispatcher` job + registration (no Redis lock)
- [x] Tests written: `OutboxBackoffTests` (4+), `OutboxDispatcherOptionsValidatorTests`, `OutboxEventTypesTests`, new `Worker.UnitTests` (`OutboxDispatcherJobTests` 3), integration `OutboxDispatchRunnerTests` (15), schema/migration cases in `OutboxPersistenceTests`
- [x] Build, format, unit tests
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` clean (the generated migration was formatted).
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 456, Application 218, Worker 3, Api.UnitTests 100 passed, 0 failed.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 517 total; first run 516 passed, 1 failed (`RunOnce_TwoRunnersConcurrently_EachMessageHandledOnce`, wrong expectation, see below); after the test fix the 39 outbox tests pass.
- Finding: the claim orders by `OccurredAt, Id` but `IX_OutboxMessages_Unprocessed` is keyed on `ProcessedAt`, so the plan sorts and `UPDLOCK` covers every eligible row, not just the batch. Two dispatchers never double-handle, but while one holds a batch the others claim nothing. Keying the index on `OccurredAt` would lock only the batch; not done because the spec and `DATABASE.md` fix the key.
- `contracts/swagger.json` unchanged (no endpoint or DTO change).

### Review follow-up fixes (2026-10-14)
Applied after the review (spec 10_01 §10): handlers resolved in a scope per message (code review M2), `HandlerTimeout` and `PassBudget` options (SEC-1), outcomes saved on shutdown (code review M1), no-handler Information log (SEC-3), every "parked" log at Error (L2), "index-only" wording corrected and the replicas-take-turns limitation documented (QA M1, L1), `OutboxMessageTests` (QA L1). New integration cases: handler timeout, pass budget, host stop, scope per message.
- `dotnet build`: 0 warnings, 0 errors. Unit tests: Domain 456, Application 219, Worker 3, Api.UnitTests 105 passed, 0 failed.
- Integration tests with Docker: 521 total; 520 passed on the first full run, the one failure was a wrong exception-name assertion in the new timeout test (`TaskCanceledException`), fixed; the 43 outbox tests then pass. `dotnet format --verify-no-changes` clean.
- The runner changed after the review approved the spec; a re-review of the follow-up changes was not run.

### Review round 2 fixes (2026-10-14)
L1 outcomes always saved with `CancellationToken.None` and no message started once the host is stopping, L3 timeout recorded as `Handler timed out after ...`, L4 validator upper bounds (`BatchSize` 1000, `HandlerTimeout` 5 min, `PassBudget` 10 min, `BackoffCap` 1 day); spec 10_01 §11. New tests: 6 validator cases, `RunOnce_HostStopsWhileLastHandlerFinishes_StillSavesItsOutcome`, `RunOnce_HostAlreadyStopping_StartsNoMessageButSavesWhatIsDone`; the timeout test now asserts "timed out".
- `dotnet build` 0 warnings; `dotnet format --verify-no-changes` clean. Outbox unit tests: Application 20, Api.UnitTests 18, Worker 3 passed.
- Outbox integration tests with Docker: 45/45 passed (the first attempt errored at fixture start, Docker had dropped). The rest of the suite was not rerun.

### Decisions (2026-10-14)
One transaction per batch, at-least-once delivery (OQ 1); `MaxAttempts` 8 (OQ 3); the job tests live in a new `tests/IncidentHub.Worker.UnitTests` project, with an `IOutboxDispatchRunner` interface so the job takes a fake.

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1294/1294 passed (integration 517), 0 proposed tests required, 1 Medium, 4 Low |
| Security | Passed | 0 Critical / 0 High / 1 Medium / 3 Low |
| Senior code review | Approved | 0 Critical / 0 High / 2 Medium / 5 Low |

Verdict: **Approved**. Open non-blocking items: QA M1 (the claim orders by `OccurredAt, Id` but the index is keyed on `ProcessedAt`, so `UPDLOCK` covers every eligible row and replicas serialize; safe against double processing, record as an accepted limitation or key the index on `OccurredAt`), L1 no pure unit test for `MarkFailed`/`Park`, L2 `ValidateOnStart` untested, L3 partial handler success redelivers to every handler; SEC-1 (Medium, future) handlers run inside one transaction holding row locks with no per-pass time limit (add a pass timeout and Polly timeouts in 10_02), SEC-2 raw exception text stored in `Error` and logs (may carry addresses or webhook URLs), SEC-3 types without a handler are marked processed with no log, SEC-4 pre-existing vulnerable transitive packages (MessagePack, Pkcs, Xml, SignalR.Common); code review M1 cancellation on shutdown rolls back outcomes already recorded (up to 49 resends per deploy), M2 handlers share the runner's scoped `AppDbContext` and open transaction (document that handlers must not save on it, or give them their own scope; address both before 10_03), L1 "index-only" wording is wrong (`SELECT *` needs a key lookup), L2 inconsistent severity for "parked" logs, L3 `Error` stays after a later success, L4 same area as SEC-2.

### Review (round 2, follow-ups)
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed (after a targeted rerun; the first pass was Incomplete, Docker went down) | 43/43 outbox integration + 35/35 outbox unit tests passed; rest of the suite not rerun by design; 0 proposed tests required, 4 Low |
| Security | Passed | 0 Critical / 0 High / 1 Medium / 4 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 5 Low |

Verdict: **Approved**. Open non-blocking items, small ones worth doing with or before 10_02: L1/SEC-C a host stop after the last handler but before the save still rolls back the batch (always save with `CancellationToken.None`, drop the `stopping` flag, add a test); L3/QA the handler-timeout error is stored as "TaskCanceledException: A task was canceled." (record "Handler timed out after ..."); L4/SEC-D the validator has no upper bounds (`HandlerTimeout`, `PassBudget`, `BackoffCap`, `BatchSize`); L2 a throwing scope disposal after `MarkProcessed` leaves a row both processed and failed; L5 spec §3 still shows the old `OutboxBackoff` signature. Defer to 10_02: SEC-A (`GetServices` builds every handler per message, so one broken handler fails every message; resolve per event type from a startup registry), SEC-B (a skipped no-handler row leaves no marker; ship handlers with or before the dispatcher), SEC-E (a handler that swallows cancellation is marked processed; full exception text in logs), cooperative-only timeouts (senders need Polly timeouts), SEC-4 pre-existing vulnerable packages.

### Previous implementation (09_02)
**Progress**
- [x] Docs/tracking: spec decisions (09_02 §8), deferred/completed items, API-CONTRACT §7
- [x] Application: `IPostCommitDispatcher`, `IDashboardSummaryReader` (+ null object), `IncidentBroadcaster` + reader
- [x] Infrastructure: post-commit hook in `AppDbContext` (fresh scope, 2 s cap, logged failures)
- [x] Api: `HubPostCommitDispatcher`, `DashboardThrottle`, DI
- [x] Worker: no-op dispatcher
- [x] Tests written: `IncidentBroadcasterTests` (8), `DashboardThrottleTests` (7), integration `BroadcastTests` (11) and `PostCommitDispatchTests` (3)
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` clean.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 456, Application 204, Application 205, Api.UnitTests 90 passed, 0 failed (after the review fixes: `HubPostCommitDispatcherTests`, `PostCommitHookTests`, `HubDashboardBroadcasterTests`, one more broadcaster case).
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 499 passed, 0 failed (new: `BroadcastTests` 11, `PostCommitDispatchTests` 3). An earlier full run showed 16 failures in `AssignIncidentTests`, `CanWatchIncidentHandlerTests` and `CommentTests`; those classes pass alone and the full rerun was green, but I did not capture the cause (suspected load or overlapping runs).
- Integration tests re-run after the review fixes: 500 passed, 0 failed (one extra `BroadcastTests` case). A run in between failed all 500 at fixture start because the Docker pipe timed out; the immediate rerun was green.
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

Verdict: **Approved**. Open non-blocking items: QA M1 no test for an explicit transaction rollback (the hook runs after the real commit only because no handler opens a transaction), L4 sync `SaveChanges` blocks on the async dispatch; SEC-1 (Medium) `incident:{id}` watchers keep receiving after an incident moves to another team (extends the 09_01 stale-membership decision), SEC-4 transitive MessagePack; L1 summary and owning team take two queries (spec says one), L2 the old team is not told that an incident left it, L3 timer and scope per leading edge while the reader is the null-object. Fixed after the review: code review M1 (`IAppTeamReader.GetOwningTeamIdAsync` removed, it had no caller) and M2 (`HubPostCommitDispatcher` notifies dashboards first, sends every message and throws the collected failures; new `HubPostCommitDispatcherTests`). Second small batch fixed: SEC-2/QA L5 (the 2 s cap now cancels the dispatch token), SEC-3 (internal if the event or the stored row says so), QA L1/L2/L3 (tests), code review L4 (default no-op `IPostCommitDispatcher` in Infrastructure; the Worker class was removed). Still open: QA M1, L4; SEC-1, SEC-4; code review L1, L2, L3. The cause of the 16 transient failures in one earlier full run was not found (not reproduced in two reruns).

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

- 2026-10-14 � 10_03 � Reviewed � Approved � QA 1406/1406 (5 Low), security 0 Critical/High/Medium (3 Low), code review 5 Low.
- 2026-10-14 � 10_03 � Done � Email triggers, SEC-A, SEC-B; 535 integration + 871 unit tests green with Docker, format clean; review pending.
- 2026-10-14 � 10_03 � Blocked � Email handlers (reported, assigned, resolved), keyed handler resolution (SEC-A), no-handler marker (SEC-B); build, format and 871 unit tests green; integration run waits for Docker.
- 2026-10-14 � 10_03 � In progress � Started; email triggers on the outbox, pulls in SEC-A/SEC-B from the 10_02 review.
- 2026-10-14 � 10_02 � Fixed � Review SEC-1 (Tls mode, no credentials in clear text), SEC-2 (no inner exception), L1 (bounded disconnect); build, format, unit and Mailpit tests green.
- 2026-10-14 � 10_02 � Reviewed � Approved � QA 1356/1356 (2 Low), security 0 Critical/High (1 Medium, 4 Low), code review 5 Low.
- 2026-10-14 � 10_02 � Done � Email sender (MailKit), Polly pipeline, Mailpit, SEC-E; 831 unit + 47 integration (Mailpit + outbox) green, format clean; review pending.
- 2026-10-14 � 10_02 � Blocked � Code, docs and unit tests complete (build, format, 831 unit tests green); Mailpit integration tests wait for Docker.
- 2026-10-14 · 10_02 · In progress · Started; email sender (MailKit), Polly pipeline per channel, Mailpit; no deferred item involved.
- 2026-10-14 · 10_01 · Fixed · Review round 2 fixes (L1 shutdown save, L3 timeout text, L4 option bounds); 45/45 outbox integration + outbox unit tests green, format clean.
- 2026-10-14 · 10_01 · Blocked · Review round 2 fixes (L1, L3, L4) coded, unit tests green; integration rerun waits for Docker.
- 2026-10-14 · 10_01 · Reviewed · Approved (round 2, follow-ups) — QA 43/43 outbox integration + 35/35 unit (4 Low), security 0 Critical/High (1 Medium, 4 Low), code review 0 Critical/High/Medium (5 Low).
- 2026-10-14 · 10_01 · Fixed · Review follow-ups (handler scope, HandlerTimeout/PassBudget, shutdown save, logging, docs, OutboxMessage tests); 521 integration + unit tests green, format clean.
- 2026-10-14 · 10_01 · Reviewed · Approved — QA 1294/1294 (1 Medium, 4 Low), security 0 Critical/High (1 Medium, 3 Low), code review 2 Medium / 5 Low.
- 2026-10-14 · 10_01 · Done · Dispatcher, retry/back-off, `NextAttemptAt`; unit and integration tests green with Docker; review pending.
- 2026-10-14 · 10_01 · Blocked · Code and tests complete, unit tests green; integration run waits for Docker.
- 2026-10-14 · 10_01 · In progress · Started; outbox dispatcher, retry/back-off and `NextAttemptAt` column.
- 2026-10-14 · 09_02 · Fixed · SEC-2/L5 dispatch cancelled at the cap, SEC-3 stored-row routing, QA L1/L2/L3 tests, code review L4 default dispatcher; 500 integration + 90 Api/205 Application unit tests green, format clean.
- 2026-10-13 · 09_02 · Fixed · Review code-review M1 (unused `GetOwningTeamIdAsync` removed) and M2 (failure isolation in `HubPostCommitDispatcher`); build, unit and integration tests green (499 integration, 84 Api.UnitTests).
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
