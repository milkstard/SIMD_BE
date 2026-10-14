namespace IncidentHub.Application.Outbox;

/// <summary>Thrown by a handler for a failure that no retry can fix; the dispatcher parks the message at once.</summary>
public sealed class NonRetryableOutboxException : Exception
{
    public NonRetryableOutboxException(string message)
        : base(message)
    {
    }

    public NonRetryableOutboxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
