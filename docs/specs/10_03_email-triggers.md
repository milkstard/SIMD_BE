# Spec 10_03: Email triggers (created, assigned, resolved)

> Status: **Draft** · Source: BE-10 in docs/BACKEND-SPECS.md · Part 3 of 3

## 1. Purpose & scope

Connects the outbox engine (10_01) to the email sender (10_02) for the three BE-10 triggers:
- incident created → the application's owning team (team email),
- incident assigned → the new assignee,
- incident resolved → the reporter.

Handlers are the only callers of `INotificationSender`; request handlers never send mail (acceptance criterion).

Pulled in from `deferred-items.md`:
- "Dispatch `IncidentReported` outbox rows (email to the owning team)" (05_01, → BE-10).
- The BE-10 (email) part of "Notification delivery for `IncidentTransitioned` / `IncidentAssigned` / … rows" (07_02, 07_03, 08_01, 08_03). The rest of that row (`IncidentUpdated`, `CommentAdded`, `AttachmentAdded`, other channels) stays with BE-13/BE-14.

## 2. Dependencies

- **10_01** (`IOutboxMessageHandler`, runner), **10_02** (`INotificationSender`, renderer, `PublicBaseUrl`).
- BE-05 (`IncidentReported`), BE-07 (`IncidentAssigned`, `IncidentTransitioned`), BE-03 (`Teams.Email`), BE-02 (`Users`).

## 3. Design

### Application (`Notifications/Email/`)
One handler per trigger, each an `IOutboxMessageHandler`; recipient and content are decided here, not in the sender.
They read through `IAppDbContext` (`AsNoTracking()`, `Select` into small projections) in the dispatcher's scope.

| Handler | Event | Fires when | Recipient | Skipped when |
|---|---|---|---|---|
| `IncidentReportedEmailHandler` | `IncidentReported` | always | `Teams.Email` of the application's `OwningTeamId` | the team or incident no longer exists (logged) |
| `IncidentAssignedEmailHandler` | `IncidentAssigned` | `AssigneeId` is not null and differs from `PreviousAssigneeId` | the new assignee's `Users.Email` | assignee is the actor (self-assignment, OQ 2), user missing; a team-only or unassign change sends nothing |
| `IncidentResolvedEmailHandler` | `IncidentTransitioned` | `To == Resolved` | the reporter's `Users.Email` | reporter is the actor (OQ 2), user missing |

Content (via 10_02 renderer): subject `[INC-1042] <Severity> incident reported: <title>` / `[INC-1042] Assigned to you: <title>` / `[INC-1042] Resolved: <title>`;
facts: application, environment, severity, status, link. The resolved mail includes the transition `Note` (HTML-encoded, plain text). The description body is **not** emailed (it is rich HTML and may be large).
No internal-comment content ever appears (comments are not a trigger).

A "skipped" outcome returns normally (message counts as handled). A missing recipient is logged at `Warning` with ids, never the address.
Data read for the mail reflects the state at send time, not at the event time (OQ 3); a deleted/deactivated target skips silently.

`Notification` construction: `MessageId` = the outbox id passed to the handler, so a redelivery produces the same `Message-Id`.

### Infrastructure
- DI: the three handlers registered as `IOutboxMessageHandler` and a sender lookup by `NotificationChannel.Email` in `AddOutbox()`/`AddNotificationSenders()`.
- No schema change, no migration.

### Worker / Api
- Worker: nothing beyond DI (the handlers run inside the dispatcher). The API does not register them.

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `swagger.json` unchanged.

## 5. Acceptance criteria

- [ ] Reporting an incident results, after a dispatcher pass, in exactly one email to the owning team's address; `ProcessedAt` is set.
- [ ] Assigning an incident to a different user emails that user once; re-assigning the same user, unassigning, or changing only the team sends nothing.
- [ ] Transitioning to `Resolved` emails the reporter; transitions to other statuses send nothing.
- [ ] No request handler, controller or hub references `INotificationSender`/`EmailSender`; only dispatcher-run handlers do (architecture test).
- [ ] An SMTP outage leaves the message unprocessed with `Attempts` incremented and succeeds on a later pass once SMTP is back (end-to-end, Mailpit toggled); no mail is sent twice for a message that succeeded.
- [ ] A recipient who no longer exists, or a team without a valid address, does not fail the batch or block later messages.
- [ ] Other outbox types (`IncidentUpdated`, `CommentAdded`, `AttachmentAdded`, other transitions) are marked processed without sending.
- [ ] Email content contains no internal-comment text, HTML-encodes titles and notes, and links to `PublicBaseUrl/incidents/INC-n`.
- [ ] Two dispatcher instances against a seeded outbox send one mail per message (Mailpit count).
- [ ] `dotnet build` (warnings as errors), `dotnet format`, full test suite green; `current-status.md` updated.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Notifications/Email/` (NSubstitute sender; in-memory-free: handlers query `IAppDbContext`, so these use the SQL container fixture where a query is needed, otherwise pure builders)
- `EmailContentBuilderTests`: `Build_Reported_SubjectAndFacts`, `Build_Resolved_IncludesNoteEncoded`, `Build_TitleWithMarkup_IsEncoded`.
- `IncidentAssignedEmailHandlerTests`: `Handle_NewAssignee_SendsOnce`, `Handle_SameAssignee_SendsNothing`, `Handle_Unassign_SendsNothing`, `Handle_SelfAssignment_SendsNothing`, `Handle_AssigneeMissing_SkipsWithoutThrowing`.
- `IncidentResolvedEmailHandlerTests`: `Handle_ToResolved_EmailsReporter`, `Handle_ToOtherStatus_SendsNothing`, `Handle_ReporterIsActor_SendsNothing`.
- `IncidentReportedEmailHandlerTests`: `Handle_Reported_EmailsOwningTeam`, `Handle_TeamMissing_SkipsWithoutThrowing`.
- `Handle_SenderThrows_PropagatesForRetry` for each handler.

`tests/IncidentHub.Api.UnitTests/Architecture/`
- `NotificationLayeringTests`: `OnlyOutboxHandlers_ReferenceNotificationSender`, `Api_DoesNotRegisterOutboxDispatcher`.

`tests/IncidentHub.Api.IntegrationTests/Outbox/EmailDispatchTests` — `[Trait("Category","Integration")]`, SQL Server + Mailpit containers, real runner:
- `Report_ThenDispatch_EmailsOwningTeamOnce`
- `Assign_ThenDispatch_EmailsAssignee`, `Assign_SameUserAgain_NoSecondMail`
- `Resolve_ThenDispatch_EmailsReporter`, `Transition_ToTriaged_NoMail`
- `Dispatch_SmtpDown_KeepsMessageAndRetriesAfterRecovery` (fake `TimeProvider` advances past `NextAttemptAt`)
- `Dispatch_TwoRunners_OneMailPerMessage`
- `Dispatch_CommentAdded_MarkedProcessedWithoutMail`

## 7. Out of scope

- Email for `IncidentUpdated`, severity changes, `CommentAdded`, `@mentions`, `AttachmentAdded`, other transitions → BE-13/BE-14 as scheduled in `deferred-items.md`.
- Escalation and SLA mail (`SlaWarning`, `SlaBreached`) → BE-12.
- Teams/Slack and honouring `User.NotificationPrefs` (a `Teams` preference suppressing mail) → BE-13.
- Per-recipient digest/batching, quiet hours, unsubscribe.
- Emailing individual team members (only the team address, as the three triggers specify).

## 8. Open questions

1. **Preferences before BE-13:** `Users.NotificationPrefs` already defaults to `{"channel":"Email"}`, but BE-13 owns honouring it. Until then these handlers email regardless of the user's stored preference (the team address ignores it anyway). Confirm this interim behaviour is acceptable; the alternative is reading the preference now, which moves part of BE-13 here.
2. **Self-notification:** BACKEND-SPECS says "assigned → assignee" and "resolved → reporter" without excluding the actor. Proposed: skip when the recipient is the actor (nobody needs mail about their own action). Say if they should always be mailed.
3. **Event-time vs send-time data:** events carry ids only (05_01/08_01 decision), so titles, status and addresses are read when the mail is built. A retry hours later may show newer data (e.g. a changed title). Accepted; the alternative is widening the event payloads, a Domain change.
4. **Webhook incidents:** `IncidentReported` with `Source = Webhook` (BE-17) will mail the owning team like a manual one. Assumed intended.
5. **Team without an address:** `Teams.Email` is required in the schema, so a missing address is treated as a data error (warning + skip), not a retry.
