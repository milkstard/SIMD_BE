using IncidentHub.Domain.Common;

namespace IncidentHub.Application.Outbox;

/// <summary>
/// Reacts to one kind of outbox message (email, in-app, Teams, ...). A message may have several handlers; the dispatcher runs all of
/// them. Register a handler as a <i>keyed</i> service (<c>AddKeyedScoped&lt;IOutboxMessageHandler, T&gt;(eventType)</c>, key =
/// <see cref="EventType"/>): the dispatcher resolves only the handlers of the message's type. Delivery is at-least-once, so a handler is idempotent by <c>outboxMessageId</c> as far as its channel allows.
/// Throw to request a retry, <see cref="NonRetryableOutboxException"/> for a failure no retry can fix; returning normally means
/// done or deliberately skipped.
/// <para>
/// Contract: handlers are resolved from a scope of their own per message, so they get their own <c>IAppDbContext</c>, outside the
/// dispatcher's claim transaction; they never touch the outbox rows. They must honour the cancellation token: it fires after
/// <c>OutboxDispatcherOptions.HandlerTimeout</c> (counted as a failed attempt) and when the host stops. The text of an exception
/// is stored in <c>OutboxMessages.Error</c> and logged, so it must not contain recipient addresses, webhook URLs or secrets.
/// </para>
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>The simple class name of the domain event this handler wants, e.g. <c>IncidentReported</c>.</summary>
    string EventType { get; }

    Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken);
}
