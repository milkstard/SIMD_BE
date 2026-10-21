# Spec 13_02: Channel routing and user preferences

> Status: **Approved** · Source: BE-13 in docs/BACKEND-SPECS.md · Part 2 of 2

## 1. Purpose & scope

Connects the Teams and Slack senders (13_01) to the five notifications that already email (10_03, 12_03) and honours
`User.NotificationPrefs`: a user who prefers Teams or Slack gets the chat post **instead of** the email for the same event.

In scope (decided with the user on 2026-10-21):
- Reading `NotificationPrefs` (`{"channel":"Email"|"Teams"|"Slack"}`) and choosing the leg per recipient.
- The five existing triggers only: `IncidentReported`, `IncidentAssigned`, `IncidentTransitioned` (to Resolved), `SlaWarning`, `SlaBreached`.
- Team-addressed events (the reported mail, and the breach fallback to the owning team) go to the team's email **and** to its channel when it has a webhook.
- Fallback to email when a user's chosen chat channel cannot be used, so an alert is never lost.

Pulled in from `deferred-items.md`:
- "Honour `User.NotificationPrefs` (a `Teams` preference suppresses the email) in the email handlers" (10_03, 12_03).
- "Teams/Slack delivery of `SlaWarning` / `SlaBreached`" (12_03).
- The chat half of "Notification delivery for `IncidentAssigned` / `IncidentTransitioned`" (07_02, 07_03): the Assigned and Resolved triggers only. The other event types stay deferred (section 7).

## 2. Dependencies

- **13_01** (senders, `ChatWebhook.Classify`, pipelines), **10_01-10_03** (`IOutboxMessageHandler`, keyed registration, `EmailDelivery`, `IIncidentEmailReader`, `EmailContentBuilder`), **12_03** (SLA handlers).
- BE-02/BE-03 (`Users.NotificationPrefs`, `Teams.TeamsChannelUrl`), BE-12 (`Applications.EscalationUserId`).

## 3. Design

### Where the logic lives
Keeping **one handler per event type** matters: the dispatcher hands a message to every handler registered for its type and redelivers to all of them
after a failure (10_01 review L3). A separate "chat handler" next to each email handler would re-send the email whenever the chat post failed. So the five
existing handlers stay, and a router inside them chooses the leg(s).

### Application (`Notifications/`)
- `NotificationPrefsParser` (static, pure): `NotificationChannel? Parse(string? json)` reads `channel` case-insensitively (`Email`, `Teams`, `Slack`);
  `null` for malformed JSON, a missing property or an unknown value. The caller treats `null` as Email and logs a `Warning` with the user id (never the JSON).
- `NotificationRouter` (scoped, replaces the direct `EmailDelivery` call in the handlers; `EmailDelivery` stays as its email leg):
  - `SendToUserAsync(outboxId, user, owningTeamId, content, ct)`
    - preference Email (or `null`): email only, as today.
    - preference Teams or Slack: destination = the incident's **owning team's** `TeamsChannelUrl`. Use chat **only** when `ChatWebhook.Classify(url)` equals the preferred channel; send the chat post and **no email**.
    - the chat leg cannot be used (no URL, invalid URL, URL of the other kind) or fails permanently (`NonRetryableOutboxException`, e.g. webhook revoked): `Warning` (ids and channel only, never the URL), then send the email instead.
    - a transient chat failure (`NotificationDeliveryException`) propagates: the dispatcher retries the whole message; no email was sent meanwhile.
  - `SendToTeamAsync(outboxId, team, content, ct)`
    - always email to the team address first (any failure propagates, as today).
    - then, if the team has a usable `TeamsChannelUrl`, a chat post on the channel `ChatWebhook.Classify` returns. A permanent chat failure is logged at `Warning` and swallowed (the mail is delivered, a revoked webhook must not park every message); a transient one propagates.
- `IIncidentEmailReader` (names stay, scope grows): `EmailRecipient` gains `string? NotificationPrefs` (filled for users only); a new `GetTeamChannelUrlAsync(teamId)` returns `Teams.TeamsChannelUrl` (null when none). Same send-time read as 10_03.
- Chat notification build: render once with `IEmailTemplateRenderer` (title, facts, link), then `new Notification(outboxId, url, null, subject, text, "")` via a small `ChatDelivery` that picks the sender by `NotificationChannel`.
- Recipient to leg, per handler (handler class names are unchanged, see Open question 3):

  | Handler | Recipient | Leg |
  |---|---|---|
  | `IncidentReportedEmailHandler` | owning team | `SendToTeamAsync` |
  | `IncidentAssignedEmailHandler` | new assignee | `SendToUserAsync` |
  | `IncidentResolvedEmailHandler` | reporter | `SendToUserAsync` |
  | `SlaWarningEmailHandler` | assignee | `SendToUserAsync` |
  | `SlaBreachedEmailHandler` | escalation contact, else owning team | `SendToUserAsync`; the 12_03 team fallback uses `SendToTeamAsync` |

  Every existing skip rule (actor never notified, stale warning, missing user) runs **before** the router and is unchanged.
- The chat leg uses the same `MessageId` (outbox id) and the same content builder, so text, facts and link match the email (no internal comment, no description).

### Domain
No change. (`User.NotificationPrefs` already exists with default `{"channel":"Email"}`.)

### Infrastructure
- DI in `AddNotificationSenders`: register `NotificationRouter`, `ChatDelivery`; the five keyed handlers now depend on the router. No schema change, no migration.

### Worker / Api
- Nothing beyond DI. The Api registers no handler or sender; `NotificationLayeringTests` keeps asserting that only dispatcher-run handlers reach `INotificationSender`.

### Docs updated in this part
- `DATABASE.md`: `Users.NotificationPrefs` accepted values (`Email` | `Teams` | `Slack`) and that `Teams.TeamsChannelUrl` holds a Teams or Slack webhook.
- `API-CONTRACT.md`: the `Team.teamsChannelUrl` note (a Teams or Slack incoming webhook, https). Text only; no schema change, so `swagger.json` is unchanged.
- `CLAUDE.md`: `Notifications:Chat:*` in the local-config notes.

## 4. Endpoints & DTOs

None. No API change, so `swagger.json` is unchanged (`OpenApiDocumentTests` stays green). There is **no** endpoint to set `NotificationPrefs`
(decided: out of scope); until one exists the value changes only in the database.

## 5. Acceptance criteria

- [x] A user whose `NotificationPrefs` is Teams receives the chat post and **no email** for the same event, for each of the four user-addressed triggers (assigned, resolved, SLA warning, SLA breach to the contact).
- [x] The same holds for Slack, using a Slack webhook URL on the owning team.
- [x] A user with Email, the default, or an unparsable value receives the email only; an unparsable value logs one `Warning` with the user id.
- [x] A Teams/Slack user whose owning team has no URL, an invalid URL, or a URL of the other kind receives the email instead, with a `Warning`.
- [x] A chat post that fails permanently (404/410/403) falls back to email for a user and is logged and swallowed for a team post; the message is marked processed, not parked.
- [x] A transient chat failure leaves the message unprocessed with `Attempts` incremented; a user-path retry succeeds later with no email sent in the meantime; the dispatcher keeps running and later messages are not blocked.
- [x] An incident-reported message mails the team address and, when the team has a usable URL, also posts to its channel; a team without a URL only mails.
- [x] A breach for an application without an escalation contact reaches the owning team by email and channel, as in 12_03.
- [x] The other outbox types (`IncidentUpdated`, other transitions, `CommentAdded`, `AttachmentAdded`) are still marked processed without any send, email or chat.
- [x] No request handler, controller or hub references `INotificationSender` or a chat client; only dispatcher-run handlers do (architecture test).
- [x] A redelivered message produces the same email `Message-Id` as before.
- [x] The webhook URL appears in no log, exception text or `OutboxMessages.Error`.
- [x] `dotnet build` (warnings as errors), `dotnet format`, full test suite green; `current-status.md` updated.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Notifications/`
- `NotificationPrefsParserTests` (theory): `Parse_ValidChannel_CaseInsensitive`, `Parse_Default_ReturnsEmail`, `Parse_Malformed_ReturnsNull`, `Parse_UnknownChannel_ReturnsNull`, `Parse_MissingProperty_ReturnsNull`.
- `NotificationRouterTests` (NSubstitute senders and reader): `SendToUser_Email_SendsEmailOnly`, `SendToUser_Teams_SendsChatOnly`, `SendToUser_Slack_SendsChatOnly`, `SendToUser_TeamsPrefButSlackUrl_FallsBackToEmail`, `SendToUser_NoUrl_FallsBackToEmail`, `SendToUser_ChatPermanentFailure_FallsBackToEmail`, `SendToUser_ChatTransientFailure_PropagatesAndSendsNoEmail`, `SendToUser_BadPrefs_EmailsAndWarns`, `SendToTeam_WithUrl_SendsEmailThenChat`, `SendToTeam_NoUrl_SendsEmailOnly`, `SendToTeam_ChatPermanentFailure_Swallowed`, `SendToTeam_ChatTransientFailure_Propagates`, `SendToTeam_EmailFails_ChatNotAttempted`.
- The five handler test classes: updated to the router fake; one new case each for a chat-preferring recipient (`Handle_TeamsPreference_UsesChatLeg`). Existing skip cases unchanged.

`tests/IncidentHub.Api.UnitTests/Architecture/`
- `NotificationLayeringTests`: `OnlyOutboxHandlers_ReferenceNotificationSender` (now also covers the router), `Api_DoesNotRegisterChatSenders`.

`tests/IncidentHub.Api.IntegrationTests/Outbox/ChatDispatchTests`: `[Trait("Category","Integration")]`, SQL Server + Mailpit containers, the real dispatcher runner, **recording fake senders** for `Teams` and `Slack` (the HTTP leg is covered in 13_01):
- `Assign_TeamsUser_ThenDispatch_PostsToTeamsAndSendsNoMail`
- `Resolve_SlackUser_ThenDispatch_PostsToSlackAndSendsNoMail`
- `SlaBreached_TeamsContact_ThenDispatch_PostsToChannel`
- `Report_TeamWithUrl_ThenDispatch_MailsAndPosts`, `Report_TeamWithoutUrl_ThenDispatch_MailsOnly`
- `Assign_TeamsUserWithoutUrl_ThenDispatch_FallsBackToMail`
- `Dispatch_ChatTransientFailure_RetriesAndDoesNotMail` (fake `TimeProvider` past `NextAttemptAt`)
- `Dispatch_ChatRevoked_UserGetsMailAndMessageIsProcessed`
- `Dispatch_TwoRunners_OneDeliveryPerMessage`
- `Dispatch_CommentAdded_StillMarkedProcessedWithoutSending`

## 7. Out of scope

- Chat or email for `IncidentUpdated`, other transitions, `CommentAdded`, `@mentions`, `AttachmentAdded`: those rows stay in `deferred-items.md` and move to BE-14 (in-app wording for those events). BE-13 changes only the channel of the five existing triggers.
- An endpoint or admin screen to change `User.NotificationPrefs` (recorded in deferred items, unassigned).
- Per-user direct messages (a webhook posts to a channel, not a person), Slack bot/OAuth, Microsoft Graph chats.
- Per-application webhook URLs or a separate Slack column (see Open question 1 of 13_01).
- Quiet hours, digests, per-event preferences.

## 8. Open questions

**Decided with the user before drafting (2026-10-21):** the existing five triggers only; no preference endpoint; team-addressed events send email and chat.

1. **A "user preference" posts to a team channel.** A webhook reaches a channel, not a person, so a user who prefers Teams gets the post in their application's **owning team** channel, visible to the whole channel (including anything in the title and facts). Proposed and assumed; the alternative is not to offer per-user chat at all until direct messages exist. Confirm this reading of "per-user channel preference".
2. **Fallback to email.** BACKEND-SPECS only says a missing or invalid URL "fails gracefully (logged, retried via Polly)". Proposed: a user path falls back to email (an unreachable chat must not silence a Critical alert) after logging; a team path was already mailed, so a permanent chat failure is only logged. The alternative is to skip and rely on the dispatcher's retry, which never succeeds for a missing URL and eventually parks the message. Confirm.
3. **Handler class names.** The five handlers still say `...EmailHandler` although they can now post to chat. Renaming means touching their tests and DI keys for no behaviour change; proposed: keep the names in this part and rename in a later chore. Say if you want them renamed now.
4. **Duplicate email on a team transient chat failure.** On the team path the email is sent first; a transient chat failure then retries the whole message and mails again (same `Message-Id`, which receivers may dedupe, not guaranteed). Accepted as the 10_01 "partial success redelivers" limitation; the alternative (swallow transient chat failures) would lose the chat post.
5. **Warning on a Teams user without a team URL when the user's owning team differs from the user's own teams.** The post goes to the application's owning team channel whether or not the recipient belongs to it (an assignee always belongs to it by the assignment rule; a reporter might not be a member). Accepted: the channel is the application's, not the person's.

## 9. Decisions (2026-10-21)

Open questions 1-5 accepted as proposed: a Teams/Slack preference posts to the application's owning-team channel (1); a user path falls back to email, a team path logs and swallows a permanent chat failure (2); handler class names are kept (3); a transient chat failure on the team path may repeat the mail with the same `Message-Id` (4); the channel is the application's, not the person's (5).

Added by the implementation plan:
- **Chat disabled.** `INotificationSender` gains `bool IsLive`; the logging senders (`LoggingChatSender`, `LoggingEmailSender`) report `false`. The router treats a chat channel without a live sender like an unusable URL: a user who prefers Teams/Slack gets the email (with a `Warning`), so switching chat off never silences an alert. A team with a webhook is only mailed (`Information`).
- **Acceptance criterion added:** with `Notifications:Chat:Enabled=false`, a Teams/Slack-preferring user receives the email.
- `ChatDelivery` (Application, next to `EmailDelivery`) renders once and picks the live sender; `NotificationRouter` takes `EmailDelivery`, `ChatDelivery` and `IIncidentEmailReader`. `EmailRecipient` gains an optional `NotificationPrefs`.
- **Known limitation (code review L1).** Only a permanent chat failure falls back to email. A long Teams or Slack outage on the user path retries until `Outbox:Dispatcher:MaxAttempts` and parks the message, so that user gets no email; an operator finds it by its `Error`. Accepted: falling back on the last attempt would need the attempt count in the handlers.
- **Logging (security SEC-2).** The router logs a failed chat leg by exception type only, never `exception.Message`.
