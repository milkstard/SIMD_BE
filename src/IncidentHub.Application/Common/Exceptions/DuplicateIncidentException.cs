namespace IncidentHub.Application.Common.Exceptions;

/// <summary>
/// An open incident with the same fingerprint (application + normalized title) already exists. Mapped to 409.
/// <see cref="ExistingIncidentNumber"/> is set when the handler found it; it is null when the unique index caught a race.
/// </summary>
public sealed class DuplicateIncidentException : ConflictException
{
    private const string DefaultMessage = "An open incident with the same application and title already exists.";

    public DuplicateIncidentException()
        : base(DefaultMessage)
    {
    }

    public DuplicateIncidentException(string existingIncidentNumber)
        : base(DefaultMessage) =>
        ExistingIncidentNumber = existingIncidentNumber;

    public string? ExistingIncidentNumber { get; }
}
