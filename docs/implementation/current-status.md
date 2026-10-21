# Current implementation status

Audit of the implementation in progress, for both Claude and the user. Claude updates this file **before** starting
an implementation and again **when it is done** (or blocked). It covers only implementations from 2026-10-11 onward.

## Active

| Field | Value |
|---|---|
| Spec | `docs/specs/13_02_channel-routing-and-preferences.md` (BE-13, part 2 of 2) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-21 |
| Last updated | 2026-10-21 |
| Branch / commit | main / `8eec45c` (13_02 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (13_02 section 9), `DATABASE.md`, `API-CONTRACT.md` (text), `CLAUDE.md`, deferred/completed items
- [x] Application: `INotificationSender.IsLive`, `NotificationPrefsParser`, `ChatDelivery`, `NotificationRouter`, reader (`NotificationPrefs`, `GetTeamChannelUrlAsync`), the five handlers use the router
- [x] Infrastructure: `IsLive` on the four senders, DI for `ChatDelivery` and `NotificationRouter`
- [x] Tests written (unit: parser, router, handlers, layering, DI; integration: `Outbox/ChatDispatchTests`)
- [x] Build, unit tests
- [x] Format, full integration suite run with Docker

### Decisions (2026-10-21)
OQ 1-5 accepted as proposed (spec 13_02 section 9). Added: a chat channel without a live sender (chat disabled) falls back to email for users.

### Blockers / open questions
None. Not automated: a manual smoke test with a real Teams and a real Slack webhook. Review pending (`/review-spec`).

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` clean.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 502, Application 388, Worker 7, Api.UnitTests 238 passed, 0 failed (new: `NotificationPrefsParserTests`, `NotificationRouterTests`, a chat-preference case per handler, layering and DI cases).
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 642/642 passed in 3 m 14 s (new `Outbox/ChatDispatchTests` 15, recording Teams/Slack senders with Mailpit; `EmailDispatchTests` unchanged and green).
- `contracts/swagger.json` unchanged (no endpoint or DTO change); not regenerated.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1777/1777 passed (integration 642), 0 proposed tests required, 3 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 4 Low |

Verdict: **Approved**. Open non-blocking items: SEC-1 the host allow-list runs only at send time, so a non-allow-listed https URL is classified as Teams and each event repeats a permanent failure and a Warning before the email fallback (validate the host when the team is saved, or expose `IsAllowed` on `ChatDelivery`); SEC-2 / QA Low-1 the router logs `exception.Message` of `NonRetryableOutboxException` (safe only while the senders keep the URL out of it; log the exception type instead and add a router test); code review L1 a transient chat outage on the user path parks the message after `MaxAttempts` with no email (per spec, document it in section 9); L2 the `EmailDelivery` summary still says "three handlers"; L3 `SendToUserAsync` takes the user id apart from `EmailRecipient`; L4 the `SlaWarningEmailHandler` summary line is long; QA Low-2 the spec header still says Draft and its acceptance boxes are unticked, Low-3 the team-path duplicate mail (OQ 4) is pinned by no test. Not performed: a manual smoke test with a real Teams and a real Slack webhook, repeated full-suite runs.

## Previous implementation (13_01)

| Field | Value |
|---|---|
| Spec | `docs/specs/13_01_chat-senders-and-resilience.md` (BE-13, part 1 of 2) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-21 |
| Last updated | 2026-10-21 |
| Branch / commit | main / `b40793d` (13_01 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (13_01 section 9)
- [x] Application: `NotificationChannel.Teams/Slack`, `ChatWebhook.Classify`
- [x] Infrastructure: `ChatOptions` + validator, host allow-list, `ChatWebhookSender`, Teams and Slack senders, `LoggingChatSender`, `teams`/`slack` pipelines, DI, tracing filter for webhook URLs
- [x] Worker: `Notifications:Chat` in `appsettings.json`
- [x] Tests written (unit)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Decisions (2026-10-21)
OQ 1 owning team's `TeamsChannelUrl`, host tells Teams from Slack; OQ 2 Adaptive Card only; OQ 3 at-least-once accepted; OQ 4 missing or invalid URL is permanent (no retry, no HTTP call); OQ 5 default allow-list with operator override, send-time check only; OQ 6 reuse the renderer's plain text.

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 502, Application 343, Worker 7, Api.UnitTests 233 passed, 0 failed (new: `ChatWebhookTests`, `ChatOptionsValidatorTests`, `TeamsNotificationSenderTests`, `SlackNotificationSenderTests`, `ChatResilienceTests`, `LoggingChatSenderTests`, DI and layering cases).
- `contracts/swagger.json` unchanged (no endpoint or DTO change); not regenerated.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 627/627 passed in 3 m 6 s (no new integration cases; DI is shared). A first attempt was stopped because Docker's engine answered `docker info` with an Internal Server Error; the rerun after Docker was restarted was green.

### Blockers / open questions
None. Not automated: a manual smoke test with a real Teams and a real Slack webhook. Review pending (`/review-spec`).

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1712/1712 passed (integration 627), 0 proposed tests required, 4 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 4 Low |

Verdict: **Approved**. Open non-blocking items: QA F1 the OpenTelemetry `FilterHttpRequestMessage`/`SuppressTracing` guard has no test (removing it would pass CI); QA F3 Slack `*` and `_` in a title can break the bold markup (cannot ping anyone); QA F4 the layering test checks constructor parameters only; QA F5 redirects are tested at configuration level only (`AllowAutoRedirect = false`); SEC-1 the default Teams hosts (`*.logic.azure.com`, `*.api.powerplatform.com`) match any tenant and the port is not restricted (operators override `Notifications:Chat:AllowedHosts`; optionally require the default port); SEC-2 a bare URL in a title is auto-linked by Teams and Slack; code review L1 a Teams `TextBlock` may not show single line breaks and `EscapeMarkdown` escapes characters inside the incident link (confirm with the manual smoke test, then split the text per line); L2 the catch-all in `PostAsync` maps programming errors to a retryable failure with only the type name; L3 `ObservabilityExtensions` references the internal `ChatWebhookSender.SuppressTracing` (move the key to `Telemetry`); L4 `HostAllowList` accepts public-suffix wildcards such as `*.co.uk`. Not performed: a manual smoke test with a real Teams and a real Slack webhook (not automated), `dotnet format --verify-no-changes` by the reviewers (run by the implementer, clean), running the Worker host end to end with `Chat:Enabled=true`. The spec header still says Draft.

## Previous implementation (12_04)

| Field | Value |
|---|---|
| Spec | `docs/specs/12_04_worker-change-propagation.md` (BE-12, part 4 of 4) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-19 |
| Last updated | 2026-10-19 |
| Branch / commit | main / `aa707b7` (12_04 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (12_04 section 9), `API-CONTRACT.md` section 7 notes, deferred/completed items
- [x] Infrastructure: `IncidentChangeMessage`, `ProcessOrigin`, `RedisChangePublisher` (`IPostCommitDispatcher`), `RedisChangeSubscriber` (BackgroundService, relay claim), DI extensions
- [x] Worker: publisher replaces the no-op dispatcher; Api: subscriber registered
- [x] Tests written (unit, integration)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run, no changes.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 502, Application 329, Worker 7, Api.UnitTests 176 passed, 0 failed (new: `RedisChangePublisherTests` 5, `RedisChangeSubscriberTests` 13 incl. 5 malformed-payload cases, `IncidentBroadcasterTests` SLA-breach case).
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 627/627 passed in 3 m 33 s (new `Sla/WorkerChangePropagationTests` 6: client receives `IncidentUpdated`, summary cache refreshed, no `AppStatusChanged`, Redis down still commits, two API instances deliver once, API commit not relayed twice).
- Mutation check: with the relay claim disabled `Breach_TwoApiInstances_EachClientReceivesTheEventOnce` fails (2 copies), so the claim is what gives exactly-once delivery.
- A first full run failed 192 of 627 tests in 22 min; its output was truncated by my own `tail`, so the failures were not captured. The immediate rerun with full logging was green (627/627), so it looks like the Docker environment (the status notes record the same pattern in earlier specs), but the cause is not confirmed.
- `contracts/swagger.json` unchanged (no endpoint or DTO change).
- Finding (pre-existing): a connection in both `dashboard:{teamId}` and `team:{teamId}` gets an `IncidentUpdated` once per group; documented in `API-CONTRACT.md` section 7. The new tests use a client that skips `JoinDashboard` (`BroadcastClient.ConnectAsync(..., joinDashboard: false)`) to count deliveries.

### Decisions (2026-10-19)
OQ 1 Redis pub/sub on `incidenthub:changes`; OQ 2 relay `IncidentUpdated` only; OQ 3 implement now. A breach does not change the derived app status, so no status refresh (criterion 2 corrected). The existing `IPostCommitDispatcher` is the port on both sides (no new Application ports). The SignalR backplane already reaches every instance, so each message is claimed in Redis by one API instance before it is dispatched.

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1641/1641 passed (integration 627), 0 proposed tests required, 7 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 3 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 7 Low |

Verdict: **Approved**. Open non-blocking items: the subscriber loop catches only `RedisException`/`TimeoutException`, so any other exception type would stop the API host (SEC-1, QA, code review L2: catch `Exception` except cancellation); the subscription queue is never unsubscribed, so a restart after an enumeration failure leaves an unread queue buffering (L1); no payload-size or `ChangedFields` limits on a forged message (SEC-2: cap size, field count and names); the channel is unauthenticated, accepted because the SignalR backplane trusts Redis the same way (SEC-3; restrict the Redis ACL and network in BE-18); no test of the resubscribe loop and no test that the Worker resolves `RedisChangePublisher` and the API hosts `RedisChangeSubscriber` (QA); fixed 1 s delays in the "no second copy" assertions (QA); the claim winner crashing between claim and dispatch loses that message (accepted, best-effort); code review L3 the `Origin` guard is dead code in production (only protects a host that registers both), L4 the `AddChangeRelay` ordering comment is wrong and `Replace` depends on `AddInfrastructure` running first, L5 `ProcessOrigin`/`ChangeChannel` share a file with the message record, L6 a 2 s cap mid-loop drops the remaining updates of that save, L7 `PostCommitHook.Timeout` reused by the subscriber. Not performed: a manual two-process smoke test with the real Worker and API, repeated full-suite runs (one earlier run failed 192 tests with uncaptured output; QA's run and the rerun were green). Fixed after the review (2026-10-19): SEC-1/L2 (the subscriber loop catches any exception and retries), L1 (the queue is unsubscribed before a resubscribe and on shutdown), the resubscribe loop now has a test (2 cases), and the spec header is Approved. Still open: SEC-2, SEC-3, L3-L7, the DI-wiring test, the fixed 1 s delays. After these fixes the unit suite, format and the full integration suite (627/627, 3 m 34 s, with Docker) are green.

## Previous implementation (12_03)

| Field | Value |
|---|---|
| Spec | `docs/specs/12_03_sla-emails.md` (BE-12, part 3 of 4) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-19 |
| Last updated | 2026-10-19 |
| Branch / commit | main / `1634a92` (12_03 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (12_03 section 9)
- [x] Application: `IncidentEmailFacts` SLA fields, `IncidentEmailReader` (left join on `SlaPolicies`), `EmailContentBuilder.SlaWarning/SlaBreached` (+ CR/LF collapse for every mail), `SlaWarningEmailHandler`, `SlaBreachedEmailHandler`, current-assignee check in `IncidentAssignedEmailHandler`
- [x] Infrastructure: keyed registration in `AddNotificationSenders`
- [x] Tests written (unit, layering, Mailpit integration)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 502, Application 327, Worker 7, Api.UnitTests 158 passed, 0 failed (new: `SlaWarningEmailHandlerTests`, `SlaBreachedEmailHandlerTests`, `EmailContentBuilderTests` SLA and line-break cases, one `IncidentAssignedEmailHandlerTests` case; the layering test now covers the five handlers).
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 621/621 passed (new in `EmailDispatchTests` with Mailpit: 7 - warning to assignee, unassigned, resolved before dispatch, breach to contact, breach to owning team, redelivery same `Message-Id`, title with line breaks).
- `contracts/swagger.json` unchanged (no endpoint or DTO change); `OpenApiDocumentTests` in the green run.

### Decisions (2026-10-19)
See spec 12_03 section 9: OQ 1 skip a warning with no assignee; OQ 2 breach falls back to the owning team's email; OQ 3/4 as proposed; a warning is stale unless `SlaWarnedAt == event.OccurredAt`; the Assigned mail checks the current assignee (10_03 SEC-2); title line breaks collapsed for every mail (10_03 SEC-1).

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1615/1615 passed (integration 621), 0 proposed tests required, 5 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 4 Low |

Verdict: **Approved**. Fixed after the review: SEC-1 (`Note` collapsed with `OneLine`, new test) and code review L3 (no defaults on the new facts parameters). Open non-blocking items: SEC-2 / QA the breach handler does not check that the incident is still open (the spec does not ask for it; document it, or add the check and `IsSlaBreached` to the facts); code review L1 the warning handler logs one `Warning` for three causes (resolved, closed, superseded), L2 spec section 3 lists `IsSlaBreached` but the reader omits it (YAGNI, edit the spec), L4 `DueAt` falls back to "unknown" (untested); QA Lows: spec header still says Draft and its acceptance boxes are unticked, spec section 6 names/locations differ from the integration tests (`Outbox/EmailDispatchTests.cs`, `SlaBreached_Redelivery_SameMessageId`), no warning-specific redelivery test, the Unicode line separators (U+0085, U+2028, U+2029) of the `LineBreaks` regex are untested (code review confirmed the class holds the real characters), one transient Docker start-up failure on the first combined run (cleared on rerun).

## Previous implementation (12_02)

| Field | Value |
|---|---|
| Spec | `docs/specs/12_02_sla-monitor.md` (BE-12, part 2 of 4) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-19 |
| Last updated | 2026-10-19 |
| Branch / commit | main / `b82fe43` (12_02 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (12_02 section 9), `DATABASE.md` (`SlaWarnAt`, `SlaWarnedAt`, index, outbox types)
- [x] Domain: `SlaDueTimes.WarnAt`, `Incident.SlaWarnAt/SlaWarnedAt`, `MarkSlaWarned`, `SlaWarning`/`SlaBreached` events, `SlaEvaluation`
- [x] Application: `Sla/EvaluateSla` command + handler, `IAppDbContext.ChangeTracker`, callers pass `WarnAt`
- [x] Infrastructure: configuration, migration `AddIncidentSlaWarn` (columns, backfill, `IX_Incidents_SlaCandidates`)
- [x] Worker: `SlaMonitor` job (Redis lock `sla-monitor`, 1 min), registration, span `sla.evaluate`
- [x] Tests written (Domain, Worker unit, integration, opt-in plan test)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 502, Application 306, Worker 7, Api.UnitTests 158 passed, 0 failed (new: `SlaEvaluationTests`, `SlaClockTests` warn cases, `IncidentTests` warn/breach cases, `SlaMonitorJobTests` 3).
- `contracts/swagger.json`: regenerated with `scripts/generate-openapi.ps1`, no diff.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 614/614 passed (new: `EvaluateSlaHandlerTests` 10, `SlaMonitorTests` 4). The opt-in `SlaCandidatesQueryPlanTests` passed: the candidate query is an `Index Seek` on `IX_Incidents_SlaCandidates` (50k rows, pass 164 ms).
- Finding: SQL Server rejects `NOT IN` in a filtered-index predicate (`Incorrect syntax near 'NOT'`, which failed every integration test at fixture start). The index filter is now `[IsSlaBreached] = 0 AND [Status] <> 'Resolved' AND [Status] <> 'Closed'` (configuration, migration, designer, snapshot, `DATABASE.md`). The migration backfill SQL ran in these tests (migrations are applied at fixture start).

### Decisions (2026-10-19)
Open questions 1-6 accepted as proposed; handler tests in `Api.IntegrationTests` (spec 12_02 section 9). The integration project does not reference the Worker (its `Program`/`appsettings.json` would clash with the Api's), so the lock contention criterion is covered by `SlaMonitorJobTests` (fake lock) plus the existing `RedisDistributedLockTests`; the integration tests drive `EvaluateSlaHandler` directly.

### Blockers / open questions
None. Review pending (`/review-spec`).

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1587/1587 passed (integration 614) + plan test 1/1, 0 proposed tests required, 5 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 4 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 3 Low |

Verdict: **Approved**. Open non-blocking items: SEC-1 the lock TTL (2 min) is not renewed and a pass has no time or work bound (add a per-pass deadline or page cap, like the outbox `PassBudget`); SEC-2 the migration backfill leaves `SlaWarnAt` NULL for a severity with no policy row (COALESCE to 80) and bursts notifications on the first tick; SEC-3 `IAppDbContext.ChangeTracker` exposes the whole tracker (a narrow `ClearTracked()` would do); SEC-4 a poison incident logs an Error every minute; code review L1 `IsSlaBreached` stays true after a severity change, so the restarted clock is never watched (state in the spec that the flag is sticky, or reset it with a history row), L2 a concurrency conflict counts as `Failed` (add a `Conflict` counter), L3 note only; QA Lows: the backfill SQL is untested (add a migrate-back/forward test), the generic exception branch of `EvaluateAsync` is untested, the real-Redis two-replica test is replaced by a fake-lock unit test (accepted deviation), test seeds use real `UtcNow`, the candidate query reads the whole filtered index each tick (fine at 50k rows); the spec header still says Draft.

## Previous implementation (12_01)

| Field | Value |
|---|---|
| Spec | `docs/specs/12_01_sla-policies-table-and-admin-api.md` (BE-12, part 1 of 4) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-19 |
| Last updated | 2026-10-19 |
| Branch / commit | main / `8be423f` (12_01 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (12_01 section 9), `DATABASE.md` (`RowVersion`), `API-CONTRACT.md` (`/sla-policies`), deferred/completed items, deferred/completed items
- [x] Domain: `SlaPolicySetting` (+ unit tests)
- [x] Application: `Sla/` list, get, update (validator, handler), `SlaPolicyDto`, `SlaPolicyIds`; `IAppDbContext.SlaPolicies`
- [x] Infrastructure: configuration, migration `AddSlaPolicies` (seed), `DbSlaPolicyProvider` replaces `ConfiguredSlaPolicyProvider`
- [x] Api: `Policies.CanManageSlaPolicies`, `SlaPoliciesController`, `contracts/swagger.json` regenerated
- [x] Tests written (unit + integration)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 475, Application 306, Worker 4, Api.UnitTests 156 passed, 0 failed (new: `SlaPolicySettingTests`, `SlaPolicyIdsTests`, `UpdateSlaPolicyValidatorTests`).
- `contracts/swagger.json` regenerated: +327 lines, additive (new paths and schemas only); `OpenApiDocumentTests` 5/5.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 600/600 passed (new: `SlaPoliciesEndpointsTests`, `UpdateSlaPolicyHandlerTests`, `DbSlaPolicyProviderTests`). The first attempt waited for Docker to start.

### Decisions (2026-10-19)
Entity lives in Domain (`SlaPolicySetting`) instead of Infrastructure so Application handlers can use `IAppDbContext`; open questions 1-5 accepted as proposed (spec 12_01 section 9).

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1541/1541 passed (integration 600), 0 proposed tests required, 5 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 0 Medium / 4 Low |

Verdict: **Approved**. Open non-blocking items: SEC-1 the policy update logs no actor or old/new values (inject `ICurrentUser`, or add an audit table); SEC-2 the unvalidated `{id}` is echoed in the 404 detail (use a fixed message); code review L1 a missing row makes the list return fewer than 4 items and `PUT` answer 404 (document it, or create the row from `Sla:Defaults` on update), L2 a row edited outside the API (e.g. `ack = 0`) makes `ToPolicy()` throw (add CHECK constraints or catch and fall back), L3 `SlaPolicySetting.Update` throws `ArgumentOutOfRangeException`, not `DomainException` (unreachable through the API), L4 the endpoint tests mutate shared rows and restore them in `finally`; QA Lows: no test of the concurrent-write 409 race, 401 asserted on one route only, spec header still says Draft.

## Previous implementation (11_03)

| Field | Value |
|---|---|
| Spec | `docs/specs/11_03_current-user-and-openapi.md` (BE-11, part 3 of 3) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-14 |
| Last updated | 2026-10-14 |
| Branch / commit | main / `d585515` (11_03 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (11_03 section 9), `API-CONTRACT.md` (`GET /me` notes), `CLAUDE.md` (pipeline diff-check note)
- [x] Application: `Users/GetCurrentUser` (query, `CurrentUserDto`, handler)
- [x] Api: `MeController`; Swagger: `SortedDocumentFilter` (paths and schemas), explicit action order, `range` allowed values (`OpenApiAllowedValues`)
- [x] Tests written: `GetCurrentUserHandlerTests` (3), integration `MeTests` (4), `OpenApiDocumentTests` (5, no Docker)
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker
- [x] `contracts/swagger.json` regenerated; two script runs are byte-identical

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 466, Application 284, Worker 4, Api.UnitTests 160 passed, 0 failed.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 572/572 passed (new `MeTests` 4). A first full run failed on SQL connections because the containers exited (Docker dropped, exit 255); the rerun was green.
- `scripts/generate-openapi.ps1` run twice: identical SHA-1. Semantic diff against the previous file: added `/api/v1/me`, `CurrentUserDto`, `UserRole`; changed only the `range` parameter schema (enum `24h`/`7d`/`30d`, default `7d`); no removals. The textual diff is large because paths and schemas are now sorted (one-time reorder, spec section 9 item 4).

### Decisions (2026-10-14)
See spec 11_03 section 9: OQ 1-3 accepted as proposed; paths and schemas are sorted explicitly (one-time reorder diff in `swagger.json`).

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1486/1486 passed (integration 572), 0 proposed tests required, 1 Medium, 7 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 1 Low |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 5 Low |

Verdict: **Approved**. Fixed after the review: code review M1 (the `GET /me` note moved below the table). Open non-blocking items: QA M1 `Generate_MatchesCommittedSwagger` compares the in-process document, not the `dotnet swagger tofile` output (the pipeline `git diff --exit-code` stays the real gate); QA Low-1 no negative test for token roles vs `TeamMembers.Role`; QA Low-3 the committed-swagger test returns instead of reporting Skipped when the file is absent; QA Low-4 `Guid.Order()` is .NET order, not string order; QA Low-5 schema names are `CurrentUserDto`/`UserRole`, the contract says `CurrentUser`/`Role`; QA Low-6 / code review L3 the range tokens are repeated in `DashboardController` and `DashboardRange`; code review L1 tags and enum order are not pinned, L4 role order relies on the enum values, L5 `Get_Admin_ReturnsAdminRole` uses `Contain` instead of `Equal`; SEC-1 pre-existing vulnerable transitive packages (MessagePack, Cryptography.Xml, SignalR.Common, SSH.NET in tests).

## Previous implementation (11_02)

| Field | Value |
|---|---|
| Spec | `docs/specs/11_02_dashboard-summary.md` (BE-11, part 2 of 3) |
| State | Done <!-- Planned - In progress - Blocked - Done --> |
| Started | 2026-10-14 |
| Last updated | 2026-10-14 |
| Branch / commit | main / `79750c7` (11_02 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (11_02 section 9), `API-CONTRACT.md` (summary definitions, `DashboardChanged` payload), deferred/completed items, `contracts/swagger.json` regenerated (+152 lines, additive)
- [x] Application: `DashboardRange`, `IDashboardSummaryProvider`, `DashboardSummaryQueries`, `GetDashboardSummary` query/validator/handler, `DashboardSummaryReader` (recompute + overwrite for the push)
- [x] Infrastructure: `RedisDashboardSummaryProvider` (5 s TTL, `SET NX` stampede guard, Redis errors logged and bypassed, span `dashboard.summary`)
- [x] Api: `DashboardController.GetSummary`; the API host replaces the null `IDashboardSummaryReader`, the Worker keeps it
- [x] Tests written: Application unit (`DashboardSummaryTests`), integration `DashboardSummaryTests` (theories included), `BroadcastTests.Report_Committed_DashboardChangedCarriesSummary`, opt-in `DashboardSummaryQueryPlanTests`
- [x] Build, format, unit tests
- [x] Full integration suite run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 466, Application 281, Worker 4, Api.UnitTests 155 passed, 0 failed.
- Integration with Docker, before it stopped: `DashboardSummaryTests` + `Report_Committed_DashboardChangedCarriesSummary` 19/19 passed; `DashboardSummaryQueryPlanTests` passed (open grouping is an index seek on `IX_Incidents_Status_Severity`, no clustered scan; p95 of the uncached 30d summary 197.9 ms on 50k rows). Full suite after `dotnet format`: `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` 568/568 passed.
- `contracts/swagger.json` regenerated: +152 lines, the new path and schemas only.

### Decisions (2026-10-14)
See spec 11_02 section 9: window = whole UTC days (1/7/30), the push recomputes and overwrites the cache, MTTA/MTTR averaged in memory (no `DATEDIFF` in Application).

### Blockers / open questions
None. Review pending (`/review-spec`).

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1474/1474 passed (integration 568), 0 proposed tests required, 6 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 4 Low |

Verdict: **Approved**. Fixed after the review: code review M1 (documented in API-CONTRACT section 7), SEC-1/QA Low-6 (`When.NotExists` on the miss path), L3. Open non-blocking items: SEC-2 no rate limit on a cache-miss endpoint of about 5 queries, and MTTA/MTTR load one row per qualifying incident; L1 day buckets rely on offset-0 storage; L2 the empty-team `Build` call is hard to read; L3 the handler ignores the `TryParse` result (default is `H24`, not `7d`); L4 `DashboardRange`/`DashboardRanges` naming; QA Lows: no `CacheKey` test, no UTC day-boundary, rounding or window-start test, stampede-guard paths untested.

## Previous implementation (11_01)

| Field | Value |
|---|---|
| Spec | `docs/specs/11_01_app-status-calculator.md` (BE-11, part 1 of 3) |
| State | Done <!-- Planned � In progress � Blocked � Done --> |
| Started | 2026-10-14 |
| Last updated | 2026-10-14 |
| Branch / commit | main / `7c354cb` (11_01 uncommitted) |

### Progress
- [x] Docs/tracking: spec decisions (11_01 section 9), `API-CONTRACT.md` (endpoint notes, `AppStatusChanged` recipient), deferred/completed items, `contracts/swagger.json` regenerated (additive, new path only)
- [x] Domain: `AppStatusCalculator`, `OpenIncidentFact`, `AppStatusResult`; `IncidentUpdated.PreviousApplicationId` set by `Incident.Update` on an application move
- [x] Application: `OpenIncidentFacts`, `AppStatusComputation`, `IAppStatusReader` (`GetAsync`/`RefreshAsync`), `GetAppStatusQuery` + handler, `AppStatusChangedMessage`, `IncidentBroadcaster` refreshes touched applications and plans the message only on a change
- [x] Infrastructure: `RedisAppStatusReader` (key `appstatus:{id:N}`, 60 s TTL, Redis errors logged and bypassed, spans `appstatus.get`/`appstatus.refresh`)
- [x] Api: `DashboardController` (`GET /api/v1/dashboard/app-status`), `HubPostCommitDispatcher` sends `AppStatusChanged`
- [x] Tests written: `AppStatusCalculatorTests` (8), two `Incident.Update` event cases, six new `IncidentBroadcasterTests`, `AppStatusDerivationTests` (3), integration `AppStatusTests` (8) and `AppStatusBroadcastTests` (3)
- [x] Build, format, unit tests
- [x] Integration tests run with Docker

### Verification
- `dotnet build`: 0 warnings, 0 errors. `dotnet format` run.
- `dotnet test --filter "Category!=Integration&Category!=Performance"`: Domain 466, Application 265, Worker 4, Api.UnitTests 155 passed, 0 failed.
- `dotnet test tests/IncidentHub.Api.IntegrationTests --filter Category!=Performance` with Docker: 546/546 passed (new `AppStatusTests` 8, `AppStatusBroadcastTests` 3).
- Finding: with Redis down no authenticated request answers, because `UserDirectory.EnsureUserAsync` (sign-in sync, pre-existing) needs Redis. `Get_RedisUnavailable_StillAnswers` therefore tests `RedisAppStatusReader` directly with a dead multiplexer; the endpoint-level criterion depends on a separate fix to `UserDirectory`.
- `contracts/swagger.json` regenerated: +76 lines, the new path only.

### Decisions (2026-10-14)
See spec 11_01 section 9: open questions 1-8 accepted as proposed; `PreviousApplicationId` on `IncidentUpdated`; `InvalidateAsync` became `RefreshAsync` (read previous, recompute, overwrite).

### Blockers / open questions
None.

### Review
| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed | 1436/1436 passed (integration 546), 0 proposed tests required, 3 Low |
| Security | Passed | 0 Critical / 0 High / 0 Medium / 2 Low |
| Senior code review | Approved | 0 Critical / 0 High / 1 Medium / 5 Low |

Verdict: **Approved**. Open non-blocking items: code review M1 a SQL failure inside `RefreshAsync` makes `IncidentBroadcaster.PlanAsync` throw and drops every broadcast of the save (catch and log around the status block, or run it last); L1 `Previous == null` after the 60 s TTL pushes `AppStatusChanged` although the status is unchanged (document, or skip when nothing was cached and the status is Operational); L2 span tags are `cache.hits`/`cache.misses`, spec says `cache.hit`; L3 the applications query runs twice on a cache miss; L4/SEC-1 a miss-path write or an out-of-order refresh can overwrite a fresher cache value for up to 60 s (`When.NotExists` on the miss path); L5 `TouchedApplicationsAsync` could share one loop helper; SEC-2 an owning-team change is not refreshed until the TTL; QA F1/F2 no endpoint test for Closed/Reopened, an environment edit or an application move; QA F3 the endpoint cannot survive a Redis outage because `UserDirectory.EnsureUserAsync` (sign-in sync) needs Redis, so the criterion is tested at reader level.

## Previous implementation (10_03)

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

- 2026-10-21 - 13_02 - Reviewed - Approved - QA 1777/1777 (3 Low), security 0 Critical/High/Medium (2 Low), code review 0 Critical/High/Medium (4 Low).
- 2026-10-21 - 13_02 - Done - Channel routing by `User.NotificationPrefs` for the five existing triggers (`NotificationRouter`, `ChatDelivery`, `IsLive` fallback); 642 integration + 1135 unit tests green with Docker, format clean; review pending.
- 2026-10-21 - 13_02 - In progress - Started; Teams/Slack routing by `User.NotificationPrefs` for the five existing triggers; pulls in two deferred items (prefs, SLA chat delivery).
- 2026-10-21 - 13_01 - Reviewed - Approved - QA 1712/1712 (4 Low), security 0 Critical/High/Medium (2 Low), code review 0 Critical/High/Medium (4 Low).
- 2026-10-21 - 13_01 - Done - Teams and Slack webhook senders, `teams`/`slack` Polly pipelines, host allow-list, logging fallback; 627 integration + 1085 unit tests green with Docker, format clean; review pending.
- 2026-10-21 - 13_01 - Blocked - Code, docs and tests complete; build, format and 1085 unit tests green; integration run waited for Docker.
- 2026-10-21 - 13_01 - In progress - Started; Teams and Slack webhook senders with per-channel Polly pipelines; no deferred item involved.
- 2026-10-19 - 12_04 - Fixed - Review SEC-1/L2 (subscriber retries on any exception) and L1 (unsubscribe), resubscribe test, spec header Approved; build 0 warnings, format clean, unit tests and 627/627 integration tests green with Docker.
- 2026-10-19 - 12_04 - Reviewed - Approved - QA 1641/1641 (7 Low), security 0 Critical/High/Medium (3 Low), code review 0 Critical/High/Medium (7 Low).
- 2026-10-19 - 12_04 - Done - Worker-to-API change relay over Redis pub/sub (`RedisChangePublisher`, `RedisChangeSubscriber`, one relayer per message); 627 integration + 1014 unit tests green with Docker, format clean; review pending.
- 2026-10-19 - 12_04 - In progress - Started; Worker-to-API change relay over Redis pub/sub; pulls in two deferred items (cache invalidation, SLA-breach broadcast).
- 2026-10-19 - 12_03 - Fixed - Review SEC-1 (Resolved mail `Note` collapsed to one line) and code review L3 (no null defaults on the new `IncidentEmailFacts` parameters); build 0 warnings, format clean, 995 unit + 621 integration tests green with Docker.
- 2026-10-19 - 12_03 - Reviewed - Approved - QA 1615/1615 (5 Low), security 0 Critical/High/Medium (2 Low), code review 0 Critical/High/Medium (4 Low).
- 2026-10-19 - 12_03 - Done - SLA warning/breach emails (`SlaWarningEmailHandler`, `SlaBreachedEmailHandler`), 10_03 SEC-1/SEC-2 closed; 621 integration + 994 unit tests green with Docker, format clean; review pending.
- 2026-10-19 - 12_03 - In progress - Started; SLA warning/breach emails on the outbox; no deferred item involved.
- 2026-10-19 - 12_02 - Reviewed - Approved - QA 1587/1587 (5 Low), security 0 Critical/High/Medium (4 Low), code review 0 Critical/High/Medium (3 Low).
- 2026-10-19 - 12_02 - Done - SLA monitor (`SlaMonitor`, `EvaluateSla`, `SlaWarnAt`/`SlaWarnedAt`, `IX_Incidents_SlaCandidates`); 614 integration + 973 unit tests green with Docker, format clean; review pending.
- 2026-10-19 - 12_02 - Blocked - Code, docs, migration and tests complete; build, format and 973 unit tests green; integration run waits for Docker.
- 2026-10-19 - 12_02 - In progress - Started; SLA monitor (warning and breach detection), `SlaWarnAt`/`SlaWarnedAt` columns; no deferred item involved.
- 2026-10-19 - 12_01 - Reviewed - Approved - QA 1541/1541 (5 Low), security 0 Critical/High/Medium (2 Low), code review 0 Critical/High/Medium (4 Low).
- 2026-10-19 - 12_01 - Done - `SlaPolicies` table, `DbSlaPolicyProvider`, `GET/PUT /sla-policies`; 600 integration + 941 unit tests green with Docker, format clean; review pending.
- 2026-10-19 - 12_01 - Blocked - Code, docs, migration and tests complete; build, format and unit tests green; integration run waits for Docker.
- 2026-10-19 - 12_01 - In progress - Started; `SlaPolicies` table, `DbSlaPolicyProvider` and `/sla-policies` admin API; pulls in two deferred items (table, provider).
- 2026-10-14 - 11_03 - Fixed - Review code-review M1 (`GET /me` note moved below the endpoint table in `API-CONTRACT.md`); `dotnet format --verify-no-changes` clean, OpenAPI tests 5/5, opt-in Performance tests 4/4 with Docker.
- 2026-10-14 - 11_03 - Reviewed - Approved - QA 1486/1486 (1 Medium, 7 Low), security 0 Critical/High/Medium (1 Low), code review 1 Medium / 5 Low.
- 2026-10-14 - 11_03 - Done - `GET /me`, sorted and deterministic OpenAPI, `range` enum documented; 572 integration + 914 unit tests green with Docker, format clean; review pending.
- 2026-10-14 - 11_03 - In progress - Started; `GET /me` and deterministic OpenAPI; no deferred item involved.
- 2026-10-14 - 11_02 - Fixed - Review SEC-1 (`When.NotExists` on the miss path), code review L3 (`TryParse` falls back to `7d`), M1 (API-CONTRACT section 7 note: multi-team clients refetch); build 0 warnings, format clean, 906 unit + 568 integration tests green with Docker.
- 2026-10-14 - 11_02 - Reviewed - Approved - QA 1474/1474 (6 Low), security 0 Critical/High/Medium (2 Low), code review 1 Medium / 4 Low.
- 2026-10-14 - 11_02 - Done - Summary endpoint, Redis provider, real `DashboardChanged` payload; 568 integration + 906 unit tests green with Docker, format clean; review pending.
- 2026-10-14 - 11_02 - Blocked - Summary endpoint, Redis provider and real `DashboardChanged` payload coded; build, format, unit tests and the new integration tests green; full integration rerun waits for Docker.
- 2026-10-14 - 11_02 - In progress - Started; pulls in the `DashboardChanged` payload (production `IDashboardSummaryReader`) from deferred items.
- 2026-10-14 - 11_01 - Committed - `79750c7`.
- 2026-10-14 � 11_01 � Fixed � Review M1 (status refresh failure no longer drops the save's broadcasts), L1 (documented), L2 (span tags), L4/SEC-1 (`When.NotExists` on the miss path), QA F1/F2 (Closed, Reopened, application-move endpoint tests), F3 recorded in deferred items; unit tests green, 28 related integration tests green, format clean. Not done: L3, L5, SEC-2, environment-edit test (apps only offer Production).
- 2026-10-14 � 11_01 � Reviewed � Approved ? QA 1436/1436 (3 Low), security 0 Critical/High/Medium (2 Low), code review 1 Medium / 5 Low.
- 2026-10-14 � 11_01 � Done � Calculator, cached reader, `GET /dashboard/app-status`, `AppStatusChanged`; 546 integration + 890 unit tests green with Docker, format clean; review pending.
- 2026-10-14 � 11_01 � In progress � Started; pulls in `AppStatusCalculator` and the `AppStatusChanged` broadcast from deferred items.
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
