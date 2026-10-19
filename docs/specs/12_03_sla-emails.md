# Spec 12_03: SLA warning and breach emails

> Status: **Draft** · Source: BE-12 in docs/BACKEND-SPECS.md · Part 3 of 4

## 1. Purpose & scope

Delivers the two `SlaWarning` / `SlaBreached` outbox messages produced by 12_02:
- warning -> the incident's **assignee**,
- breach -> the application's **escalation contact** (`Applications.EscalationUserId`).

Email only (the BE-10 sender). Handlers run inside the dispatcher, like the 10_03 handlers.

## 2. Dependencies

12_02 (events and outbox rows), 10_01-10_03 (`IOutboxMessageHandler`, `EmailDelivery`, `IIncidentEmailReader`, `EmailContentBuilder`, keyed registration in `AddNotificationSenders`).

## 3. Design

### Application (`Notifications/Email/`)
| Handler | Event | Recipient | Skipped (logged at `Warning`, ids only) when |
|---|---|---|---|
| `SlaWarningEmailHandler` | `SlaWarning` | current `AssigneeId`'s `Users.Email` | the incident is gone, no longer open, not warned anymore, or has no assignee (OQ 1) |
| `SlaBreachedEmailHandler` | `SlaBreached` | `EscalationUserId` of the incident's application | incident or application gone, no escalation contact (OQ 2), user has no address |

- `IIncidentEmailReader` gains the fields the mails need: `ResolveDueAt`, `IsSlaBreached`, `AssigneeId`, `EscalationUserId`; `IncidentEmailFacts` is extended (this also closes the 10_03 SEC-2 idea of checking the current assignee at send time).
- `EmailContentBuilder.SlaWarning(incident, percent, dueAt)` / `SlaBreached(incident, dueAt)`: subjects `[INC-1042] SLA warning: <title>` and `[INC-1042] SLA BREACHED: <title>`; facts: application, environment, severity, status, due time (UTC, ISO), link. The incident title has CR/LF collapsed (10_03 SEC-1 lesson). No description or comment content.
- The message Id is the outbox id (`Message-Id` stable on redelivery), through `EmailDelivery`.
- A skipped message returns normally (counts as handled), as in 10_03.

### Infrastructure
Register both handlers keyed by event type in `AddNotificationSenders`. No schema change.

### Worker / Api
Nothing beyond DI. The API does not register the handlers; the layering test (`NotificationLayeringTests`) keeps asserting that only dispatcher-run handlers touch `INotificationSender`.

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` / `swagger.json` unchanged.

## 5. Acceptance criteria

- [ ] A warning outbox row results, after a dispatcher pass, in exactly one email to the assignee; no email when unassigned.
- [ ] A breach outbox row results in exactly one email to the application's escalation contact.
- [ ] A warning for an incident resolved/closed in the meantime sends nothing.
- [ ] Redelivery of the same outbox row produces the same `Message-Id` (idempotent sender).
- [ ] The incident title cannot inject header or body lines; no internal comment or description text is mailed.
- [ ] No request handler, controller or hub references `INotificationSender` (architecture test stays green).

## 6. Test plan

- `Application.UnitTests/Notifications/Email/`: `SlaWarningHandler_Assigned_SendsToAssignee`, `..._Unassigned_Skips`, `..._IncidentResolved_Skips`, `SlaBreachedHandler_WithContact_SendsToContact`, `..._NoContact_Skips`, `..._UserWithoutAddress_Skips`, `EmailContentBuilderTests` for both mails (subject, facts, CR/LF title).
- `Api.IntegrationTests/Notifications/` (Mailpit, `[Trait("Category","Integration")]`): `SlaWarning_Dispatched_MailsAssignee`, `SlaBreached_Dispatched_MailsEscalationContact`, `Redelivery_SameMessageId`.
- Update `NotificationLayeringTests` if it enumerates handler types.

## 7. Out of scope

- Honouring `User.NotificationPrefs` (a `Teams` preference suppressing mail) -> BE-13 (stays in deferred items, row extended).
- In-app `SlaWarning`/`SlaBreached` notifications -> BE-14; Teams/Slack delivery -> BE-13.
- Notifying the team lead or reporter on breach.
- Repeat reminders after the first breach mail.

## 8. Open questions

1. **Warning with no assignee.** BACKEND-SPECS says "warns assignee". Proposed: skip (and log). Alternative: mail the owning team address so an unassigned Critical does not breach silently.
2. **Breach with no escalation contact** (`EscalationUserId` is nullable). Proposed: fall back to the owning team's email so a breach always reaches someone; alternative: skip and only flag. Confirm.
3. **Self-notification.** The monitor is the system, so the "actor is never mailed about their own action" rule from 10_03 does not apply. Confirm.
4. **Escalation contact = assignee.** Mail both anyway (proposed) or de-duplicate?

## 9. Decisions (2026-10-19)

1. OQ 1: a warning with no assignee is skipped and logged.
2. OQ 2: a breach without an escalation contact, or with a contact that is missing or has no address, goes to the owning team's `Teams.Email`; it is skipped (and logged) only when that is missing too.
3. OQ 3 and 4 accepted as proposed: the system is the actor, and each event has one recipient, so nothing is de-duplicated.
4. A warning is stale unless the incident's `SlaWarnedAt` equals the event's `OccurredAt` (`MarkSlaWarned(at)` raises the event with the same value). This covers "not warned anymore" after a severity change and an older warning delivered after a newer one.
5. The warning mail's percent comes from a left join on `SlaPolicies` by the incident's current severity (`IncidentEmailFacts.EscalateAtPercent`); without a policy row the fact is left out.
6. 10_03 SEC-2 closed: `IncidentAssignedEmailHandler` also skips when the incident's current assignee differs from the event's.
7. 10_03 SEC-1 closed: `EmailContentBuilder` collapses line breaks in the title for every mail (subject and `Incident` fact).
8. Test names follow the decisions: `SlaBreachedHandler_NoContact_MailsOwningTeam` replaces `..._NoContact_Skips`.
