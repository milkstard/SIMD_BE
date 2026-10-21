# Spec 13_01: Teams and Slack senders & resilience

> Status: **Draft** · Source: BE-13 in docs/BACKEND-SPECS.md · Part 1 of 2

## 1. Purpose & scope

The channel half of BE-13, mirroring what 10_02 did for email: two new `INotificationSender` implementations that post to an
incoming webhook, each behind its own Polly pipeline (timeout, retry with jitter, circuit breaker per channel). Nothing calls them
yet; 13_02 decides who gets a chat post instead of, or as well as, an email.

In scope:
- `NotificationChannel.Teams` and `NotificationChannel.Slack`, and a pure classifier that tells a Teams webhook URL from a Slack one.
- `TeamsNotificationSender` and `SlackNotificationSender` (Infrastructure), over `HttpClient`, with the failure contract of 10_02
  (`NotificationDeliveryException` retryable, `NonRetryableOutboxException` permanent).
- Options `Notifications:Chat:*`, a host allow-list for webhook URLs, validation on start, a logging fallback when disabled.
- Pipelines `teams` and `slack` built from the existing `NotificationResilience.Configure` and `Notifications:Resilience:*` options.

Decided with the user on 2026-10-21: both Teams and Slack are built; one webhook URL per team is the destination (`Teams.TeamsChannelUrl`,
documented in DATABASE.md as "MS Teams/Slack webhook"), and the sender is chosen from the URL's host.

## 2. Dependencies

- **BE-10** (done): `INotificationSender`, `Notification`, `NotificationDeliveryException`, `NonRetryableOutboxException`,
  `NotificationResilience`, `AddNotificationSenders`, `InfrastructureTelemetry.Source`, `EmailTemplateRenderer` (its plain-text body is reused).
- BE-03 (done): `Teams.TeamsChannelUrl` (absolute https URL, at most 500 chars).

## 3. Design

### Application (`Notifications/`)
- `NotificationChannel`: add `Teams`, `Slack`.
- `Notification` is **not** changed. For a chat channel `ToAddress` carries the destination webhook URL, `Subject` the title, `TextBody` the
  plain-text facts and link (the existing renderer output), and `HtmlBody` is empty. `ToName` is unused. Because the URL is a credential
  (anyone holding it can post to the channel), no sender, log, span or exception text may contain it (see Infrastructure). `MessageId`
  is the outbox id as before.
- `ChatWebhook` (static, pure): `NotificationChannel? Classify(string? url)`: `null` for a missing, non-absolute or non-https URL;
  `Slack` when the host is `hooks.slack.com`; `Teams` for any other https host. It checks shape only; the allow-list is the sender's job.
  13_02 uses it to decide the leg; the senders use it to refuse a URL of the wrong kind.

### Infrastructure (`Notifications/Chat/`)
- `ChatOptions` (section `Notifications:Chat`): `Enabled` (default `false`), `AllowedHosts` (exact host or `*.suffix`; defaults to the Slack
  host and the Teams/Power Automate webhook hosts, confirmed against Microsoft's current webhook docs at implementation). There is no
  `PublicBaseUrl` here: the incident link is already in the rendered text (from `Notifications:Email:PublicBaseUrl`).
  Validator: when `Enabled`, `AllowedHosts` is non-empty and every entry is a valid host pattern. When `Enabled = false`,
  `LoggingChatSender` is registered for `Teams` and `Slack` (logs channel, destination host and subject; sends nothing), so dev and CI
  need no webhook.
- `TeamsNotificationSender` (`Channel = Teams`) posts the Workflows-webhook shape: `{"type":"message","attachments":[{"contentType":
  "application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","version":"1.4","body":[title TextBlock (bolder), text TextBlock (wrap)]}}]}`.
  Markdown control characters in the title and body are escaped so a title cannot form a link or emphasis.
- `SlackNotificationSender` (`Channel = Slack`) posts `{"text":"*<title>*\n<text>"}` after escaping `&`, `<` and `>` (Slack's control
  characters), so a title such as `<!channel>` or `<@U123>` cannot ping anyone.
- Shared base `ChatWebhookSender` (not public): validates the destination (`ChatWebhook.Classify` equals the sender's channel, https, host in
  `AllowedHosts`), serializes the payload with `System.Text.Json`, posts it through a named `HttpClient` (`teams` / `slack`) inside the
  channel's pipeline, and maps the result:

  | Outcome | Maps to |
  |---|---|
  | 2xx | success |
  | 408, 429, 5xx, network error, timeout | `NotificationDeliveryException` (retried by Polly, then by the dispatcher) |
  | any other 4xx (400, 401, 403, 404, 410) | `NonRetryableOutboxException` (webhook revoked, channel archived, bad payload) |
  | 3xx | `NonRetryableOutboxException` (redirects are not followed) |
  | URL missing, not https, wrong kind, host not allowed | `NonRetryableOutboxException`, **no HTTP call made** |

  The primary handler is `SocketsHttpHandler { AllowAutoRedirect = false }` (an allow-listed host cannot bounce the call to an internal address).
  Error texts carry the channel, the status code and the destination **host** only: never the URL, path, query, response body or an inner
  `HttpRequestException` (it embeds the URL), as 10_02 SEC-E did for email.
- `NotificationResilience`: add `TeamsPipeline = "teams"` and `SlackPipeline = "slack"`; each registered with `AddResiliencePipeline` using
  the same `Configure(builder, options)`, so each channel has its own circuit (one broken Slack does not open the Teams circuit).
  `BrokenCircuitException` and `TimeoutRejectedException` surface as retryable `NotificationDeliveryException`, as for email.
  Permanent failures pass through Polly untouched and do not count towards the breaker.
- Telemetry: span `notification.send` with tags `channel` (`teams`/`slack`) and `recipient.host`; no URL tag.
- DI in `AddNotificationSenders`: bind and validate `ChatOptions`; register the two pipelines; when `Chat:Enabled` register the two senders
  and their named clients, else `LoggingChatSender` for both channels. Worker host only (the API sends nothing).

### Worker / Api
- Worker `appsettings.json`: `Notifications:Chat` with `Enabled: false`. No secret in config; nothing in the Api.
- `CLAUDE.md` local-config notes list `Notifications:Chat:*` (done in 13_02 with the other docs).

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` and `swagger.json` unchanged.

## 5. Acceptance criteria

- [ ] The Teams sender posts one JSON message of the Workflows card shape; the Slack sender posts `{"text": ...}` (asserted on the request body in a fake handler).
- [ ] `<!channel>`, `<@U1>` and `&` in a title are escaped in the Slack text; Markdown control characters are escaped in the Teams text.
- [ ] A missing, non-https, malformed, wrong-kind (Slack URL given to the Teams sender and the reverse) or non-allow-listed URL throws `NonRetryableOutboxException` and makes no HTTP call.
- [ ] 429, 5xx, 408, network errors and timeouts are retried by the pipeline with jitter and then surface as `NotificationDeliveryException`; other 4xx and 3xx surface as `NonRetryableOutboxException` and are not retried.
- [ ] Redirects are never followed.
- [ ] Each channel has its own circuit: repeated Slack failures open `slack` and leave `teams` closed; the circuit closes again after the break (fake `TimeProvider`).
- [ ] The webhook URL never appears in a log entry, exception message, span tag or `OutboxMessages.Error` (asserted by scanning captured logs and exception texts for the URL's path).
- [ ] With `Chat:Enabled = false` nothing is posted and the Worker starts with no chat settings; with `Enabled = true` and invalid options it fails on start naming the field.
- [ ] Only the two senders (and the logging fallback) make outbound webhook calls; no Application, Domain or Api type references the chat `HttpClient` (`LayeringTests`).
- [ ] No secret in `appsettings.json`; `dotnet build` (warnings as errors) and `dotnet format` clean; `current-status.md` updated.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Notifications/`
- `ChatWebhookTests`: `Classify_SlackHost_ReturnsSlack`, `Classify_OtherHttpsHost_ReturnsTeams`, `Classify_Http_ReturnsNull`, `Classify_NullOrMalformed_ReturnsNull`.

`tests/IncidentHub.Api.UnitTests/Notifications/` (fake `HttpMessageHandler` behind `IHttpClientFactory`)
- `ChatOptionsValidatorTests`: `Validate_EnabledWithoutAllowedHosts_Fails`, `Validate_Disabled_Succeeds`, `Validate_BadHostPattern_Fails`.
- `TeamsNotificationSenderTests`: `Send_PostsAdaptiveCard`, `Send_MarkdownInTitle_IsEscaped`, `Send_SlackUrl_ThrowsNonRetryable`, `Send_HostNotAllowed_ThrowsNonRetryableWithoutCall`, `Send_Status429_ThrowsDeliveryException`, `Send_Status404_ThrowsNonRetryable`, `Send_Redirect_ThrowsNonRetryable`, `Send_ErrorText_HasNoUrl`.
- `SlackNotificationSenderTests`: `Send_PostsTextPayload`, `Send_ControlCharactersInTitle_AreEscaped`, `Send_TeamsUrl_ThrowsNonRetryable`, `Send_Status500_ThrowsDeliveryException`, `Send_Status403_ThrowsNonRetryable`.
- `ChatResilienceTests` (fake `TimeProvider`): `Pipeline_TransientFailure_RetriesThenSucceeds`, `Pipeline_NonRetryable_NotRetried`, `Pipeline_SlackFailures_DoNotOpenTeamsCircuit`, `Pipeline_AfterBreak_HalfOpensAndCloses`, `Pipeline_SlowPost_TimesOut`.
- `LoggingChatSenderTests`: `Send_LogsHostAndSubject_NotUrl`.
- DI/start-up: `AddNotificationSenders_ChatDisabled_RegistersLoggingSenders`, `AddNotificationSenders_ChatEnabledInvalid_FailsOnStart`.
- `LayeringTests` extended: only Infrastructure chat senders reference the chat clients.

No Testcontainers case in this part: the HTTP handler is the seam, and a stub server would need either a plain-http escape in the sender or an
extra container for no extra coverage. The routing through the real dispatcher is covered in 13_02. A manual smoke test with a real Teams
and a real Slack webhook is recorded in `current-status.md` as not automated.

## 7. Out of scope

- Which events and recipients use chat, per-user preference, fallback to email → 13_02.
- Per-user direct messages, Slack bot tokens/OAuth, Microsoft Graph chats, threads, buttons, delivery receipts.
- Retrying a post whose first attempt timed out after the server accepted it (webhooks have no idempotency key, so a duplicate post is possible; accepted, see Open question 3).
- Changing the Team admin API (`teamsChannelUrl` stays an absolute https URL up to 500 chars).

## 8. Open questions

**Decided with the user before drafting (2026-10-21):** build both Teams and Slack; one webhook URL per team in `Teams.TeamsChannelUrl`.

1. **Conflict: BACKEND-SPECS says "Route by application's `TeamsChannelUrl` / Slack webhook"**, but DATABASE.md and the code put `TeamsChannelUrl` on `Teams`, not `Applications`, and there is no Slack column. Proposed: use the application's **owning team's** URL (no schema change) and tell Teams from Slack by host. The alternative is a per-application URL and a separate `SlackChannelUrl`, which is a migration and an API change. Confirm.
2. **Teams payload format.** Microsoft is retiring Office 365 connector webhooks in favour of Workflows webhooks that expect an Adaptive Card; the legacy `MessageCard` shape is not planned. Confirm at implementation against the current Microsoft docs, and say if the legacy format is still needed.
3. **At-least-once without dedupe.** Email carries a stable `Message-Id`; a webhook does not. A post that succeeded but timed out client-side is posted again on retry. Accepted for v1.
4. **BACKEND-SPECS acceptance: "Missing/invalid webhook URL fails gracefully (logged, retried via Polly)".** Proposed reading: a transient failure is retried, but a missing or invalid URL is permanent (retrying cannot fix it), so it is logged and surfaces as `NonRetryableOutboxException` with no HTTP call; 13_02 then decides the fallback. Confirm this is the intended meaning of "retried".
5. **Host allow-list.** The sender refuses a host outside `AllowedHosts` (SSRF guard, because an Admin controls the URL). Teams webhook hosts vary by tenant setup (`*.webhook.office.com`, `*.logic.azure.com`, `*.powerplatform.com`), so the defaults may need an operator override. A write-time check in `PUT /teams/{id}` (422) would give earlier feedback but needs the allow-list in the API host; left out, say if wanted.
6. **Chat text reuses the email renderer's plain text.** One format for all channels (title, facts, link). Richer cards are a later improvement.

## 9. Decisions (2026-10-21)

Open questions 1-6 accepted as proposed:
1. The destination is the owning team's `Teams.TeamsChannelUrl`; the host tells Teams from Slack. No schema or API change.
2. Teams gets the Workflows Adaptive Card shape only (no legacy `MessageCard`).
3. At-least-once without dedupe is accepted for v1.
4. A missing or invalid URL is permanent (`NonRetryableOutboxException`, no HTTP call); only transient failures are retried.
5. Default `AllowedHosts` (operator-overridable), checked at send time only; no 422 in `PUT /teams/{id}`.
6. The chat text reuses the email renderer's plain text.

Implementation notes:
- The default allow-list lives in code (`ChatOptions.DefaultAllowedHosts`) and applies when `AllowedHosts` is not configured, because binding a configured array onto a defaulted array merges by index.
- `HttpClient` default logging and the OpenTelemetry HTTP instrumentation both record the request URI; the chat clients drop the factory loggers and the instrumentation skips requests flagged with `ChatWebhookSender.SuppressTracing`.
