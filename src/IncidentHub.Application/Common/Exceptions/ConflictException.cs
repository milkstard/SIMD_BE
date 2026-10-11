namespace IncidentHub.Application.Common.Exceptions;

/// <summary>
/// The write conflicts with current state (stale RowVersion, duplicate key). Mapped to 409.
/// Not sealed: specific conflicts such as <see cref="DuplicateIncidentException"/> derive from it and reuse the same mapping.
/// </summary>
public class ConflictException(string message) : Exception(message);
