# Spec 10_02: Email sender (SMTP) & resilience

> Status: **Accepted** · Source: BE-10 in docs/BACKEND-SPECS.md · Part 2 of 3

## 1. Purpose & scope

The channel abstraction and its first implementation: `INotificationSender` and an SMTP `EmailSender` (MailKit), protected by a Polly
pipeline (timeout, retry with jitter, circuit breaker per channel). Nothing calls it yet; 10_03 wires the handlers. Decided with the user on
2026-10-14: SMTP via MailKit (no SendGrid).

In scope:
- `INotificationSender` port and notification message model (channel-agnostic, so BE-13 adds Teams/Slack senders).
- `EmailSender` over SMTP, options, validation on start, plain-text + HTML bodies built from a small renderer.
- Polly v8 resilience pipeline per channel (`Microsoft.Extensions.Http.Resilience` is already referenced; the generic `ResiliencePipeline` comes from `Polly.Core`).
- Local SMTP catcher (Mailpit) in `docker-compose.yml` and integration test fixture.

## 2. Dependencies

- **10_01** (handlers are the only callers; `NonRetryableOutboxException`).
- 05_01 / BE-01 (options + `ValidateOnStart()` pattern), observability (`InfrastructureTelemetry.Source`).

## 3. Design

### Application
- `Abstractions/INotificationSender` — `NotificationChannel Channel { get; }` and `Task SendAsync(Notification notification, CancellationToken ct)`.
- `Notifications/NotificationChannel` enum (`Email`; BE-13 adds `Teams`, `Slack`) and `Notifications/Notification` record:
  `Guid MessageId` (the outbox id, for idempotency), `string ToAddress`, `string? ToName`, `string Subject`, `string TextBody`, `string HtmlBody`.
  The model carries no incident entity; handlers build it (10_03).
- Failure contract: a sender throws `NotificationDeliveryException` (retryable, default) or `NonRetryableOutboxException` (permanent: invalid address, 5xx-permanent SMTP reply 550/553).

### Infrastructure (`Notifications/`)
- `EmailOptions` (section `Notifications:Email`): `Host`, `Port`, `Tls` (`None` | `StartTls` | `SslOnConnect`; `None` is refused when `Username` is set), `Username`, `Password` (user-secrets/Key Vault only), `FromAddress`, `FromName`, `PublicBaseUrl` (for incident links), `Enabled`.
  Validator: host/port/from valid when `Enabled`. When `Enabled = false` a `LoggingEmailSender` is registered instead (logs recipient and subject, sends nothing), so dev and CI need no SMTP.
- `EmailSender : INotificationSender` (`Channel = Email`): builds a MailKit `MimeMessage` (multipart text + HTML), sets `Message-Id` = `<{MessageId}@incidenthub>` and an `X-IncidentHub-Message` header with the id (lets a receiver or relay dedupe a redelivery),
  connects, authenticates when credentials exist, sends, disconnects — one short-lived `SmtpClient` per send (no pooling in v1).
  Header values are stripped of CR/LF (no header injection from titles).
  Maps `SmtpCommandException` permanent codes and `ParseException`/invalid address to `NonRetryableOutboxException`; everything else to `NotificationDeliveryException`.
- `Resilience/NotificationResilience`: one named `ResiliencePipeline` per channel (key `email`): per-attempt timeout 10 s, retry 3× with exponential delay and jitter (transient `NotificationDeliveryException` only), circuit breaker (50% failure ratio, min throughput 5, 30 s break), outer total timeout 45 s. `BrokenCircuitException` surfaces as a retryable failure, so the dispatcher's own back-off handles an open circuit.
  Options `Notifications:Resilience:*` with defaults; the pipeline is registered with `AddResiliencePipeline`.
- `Rendering/EmailTemplateRenderer` — pure string templates (no Razor): `Render(subjectLine, headline, facts, incidentUrl)` returns text + HTML; every dynamic value is HTML-encoded. Incident link = `PublicBaseUrl/incidents/{number}`.
- Telemetry: span `notification.send` (tag `channel`) per send; never log the body or address in full (domain only).
- DI: `AddNotificationSenders(IConfiguration)` in Infrastructure, called from the Worker `Program.cs` only (not from `AddInfrastructure`). Package: `MailKit` (added to `Directory.Packages.props`).

### Worker / Api
- Worker `appsettings.json`: `Notifications:Email` with `Enabled: false`, `Host: localhost`, `Port: 1025`, `FromAddress: noreply@incidenthub.local`, `PublicBaseUrl: http://localhost:5173`; no credentials. The API does not need the sender; it is registered in the Worker host only.

### Docker
`docker-compose.yml`: add `mailpit` (SMTP `1025`, UI `8025`, healthcheck). CLAUDE.md "Commands" and local-config notes get the new keys.

## 4. Endpoints & DTOs

None. `API-CONTRACT.md` / `swagger.json` unchanged.

## 5. Acceptance criteria

- [ ] `EmailSender` is the only type that talks SMTP; no handler, controller or Application type references MailKit (`LayeringTests`).
- [ ] A sent mail has the expected recipient, subject, plain-text and HTML parts, and a `Message-Id` derived from the outbox id (verified against Mailpit's API).
- [ ] Dynamic values are HTML-encoded in the HTML part; a title containing CR/LF cannot inject a header.
- [ ] Transient SMTP failures (connection refused, timeout, 4xx) are retried by the Polly pipeline with jitter, then surface as a retryable exception; a permanent 5xx/invalid address surfaces as `NonRetryableOutboxException` and is not retried by Polly.
- [ ] The circuit breaker opens after repeated failures and rejects calls fast while open; it closes again after the break (fake `TimeProvider`).
- [ ] With `Enabled = false` nothing is sent and the host starts without SMTP settings; with `Enabled = true` and invalid settings the Worker fails on start naming the field.
- [ ] No secret in `appsettings.json`; build (warnings as errors) and `dotnet format` clean; `docker compose up -d` starts Mailpit healthy.

## 6. Test plan

`tests/IncidentHub.Application.UnitTests/Notifications/`
- `NotificationTests`: `Ctor_EmptyRecipient_Throws`.

`tests/IncidentHub.Api.UnitTests/Notifications/` (Infrastructure-facing unit tests)
- `EmailOptionsValidatorTests`: `Validate_EnabledWithoutHost_Fails`, `Validate_Disabled_Succeeds`, `Validate_InvalidFromAddress_Fails`.
- `EmailTemplateRendererTests`: `Render_HtmlSpecialCharacters_AreEncoded`, `Render_IncludesIncidentLink`.
- `EmailSenderTests` (fake SMTP transport behind a small internal `ISmtpTransport` seam): `Send_CrLfInSubject_IsStripped`, `Send_SetsMessageIdFromOutboxId`, `Send_PermanentReply_ThrowsNonRetryable`, `Send_TransientReply_ThrowsDeliveryException`.
- `NotificationResilienceTests` (fake `TimeProvider`): `Pipeline_TransientFailure_RetriesThenSucceeds`, `Pipeline_NonRetryable_NotRetried`, `Pipeline_RepeatedFailures_OpensCircuit`, `Pipeline_AfterBreak_HalfOpensAndCloses`, `Pipeline_SlowSend_TimesOut`.

`tests/IncidentHub.Api.IntegrationTests/Notifications/EmailSenderSmtpTests` — `[Trait("Category","Integration")]`, Mailpit Testcontainer:
- `Send_RealSmtp_DeliversMultipartMessage`, `Send_SmtpDown_ThrowsDeliveryException`.
- `LayeringTests` extended: Application/Domain do not reference MailKit.

## 7. Out of scope

- Which events send mail, recipients, wording → 10_03.
- Teams/Slack senders, per-user channel preference (`NotificationPrefs`) → BE-13.
- SendGrid or any HTTP email API (decided: SMTP only).
- Bounce/complaint handling, delivery receipts, unsubscribe links, localisation.
- Connection pooling / batching of SMTP sessions.

## 8. Open questions

1. **Credentials in Azure:** `Notifications:Email:Password` comes from Key Vault in Azure and user-secrets locally (CLAUDE.md); no doc names the production SMTP relay (Azure Communication Services SMTP, Office 365, other). Left as configuration.
2. **Polly packages:** CLAUDE.md requires "timeout, retry with jitter, circuit breaker per channel"; `Microsoft.Extensions.Http.Resilience` brings `Polly.Core` transitively but the repo has no central `Polly.Extensions` reference. Plan: reference `Microsoft.Extensions.Resilience` (same family, version already pinned at 8.10.0 for Http) rather than add a new package family — confirm at implementation.
3. **Retry layering:** Polly retries inside one dispatcher attempt (about 3 quick tries) and the dispatcher's back-off retries across attempts (10_01); the product is up to 4 × 8 SMTP tries per message in the worst case. Acceptable, flagged so it is a conscious choice.

## 9. Decisions (2026-10-14)

1. **Registration:** `AddNotificationSenders` is called from the Worker `Program.cs` only; the API never binds or validates `Notifications:Email`.
2. **Timeouts:** `Outbox:Dispatcher:HandlerTimeout` default raised from 30 s to 60 s so Polly's 45 s total timeout fits inside the dispatcher's. Row locks are held for at most `PassBudget` + `HandlerTimeout` = 120 s.
3. **Polly packages (OQ 2):** `Microsoft.Extensions.Resilience` 8.10.0 is referenced explicitly (brings `Polly.Extensions`/`Polly.Core`). **MailKit** 4.18.1.
4. **SEC-E (from the 10_01 review):** sender exceptions carry the SMTP status code and the recipient's domain only; never the full address, the server's reply text, or an inner `SmtpCommandException`.
5. **Retry layering (OQ 3):** accepted as written (up to 4 Polly tries x 8 dispatcher attempts).
6. **Carried to 10_03:** SEC-A (resolve handlers per event type) and SEC-B (marker for rows without a handler). SEC-4 (vulnerable transitive packages) stays a separate chore.
7. **Authentication failures** are retryable (a configuration problem a fix resolves), so they do not park every message.
8. **Package pin:** MimeKit 4.18.1 depends on `System.Security.Cryptography.Pkcs` 10.0.0 (known advisories); Infrastructure pins 10.0.13 until MimeKit updates.
9. **Retries disabled:** `MaxRetries = 0` omits the retry strategy (Polly rejects zero attempts).
10. **Review fixes (2026-10-14):** SEC-1 `UseStartTls` replaced by `Tls` (`None`/`StartTls`/`SslOnConnect`) and the validator refuses `None` with a `Username`; SEC-2 generic failures carry no inner exception; code review L1 the post-send disconnect has its own 5 s token and the SMTP client a 15 s I/O timeout.
