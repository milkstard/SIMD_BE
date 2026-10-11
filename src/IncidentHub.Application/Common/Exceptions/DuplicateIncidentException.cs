namespace IncidentHub.Application.Common.Exceptions;

/// <summary>An open incident with the same fingerprint (application + normalized title) already exists. Mapped to 409.</summary>
public sealed class DuplicateIncidentException()
    : ConflictException("An open incident with the same application and title already exists.");
