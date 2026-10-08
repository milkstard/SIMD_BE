namespace IncidentHub.Application.Common.Exceptions;

/// <summary>The write conflicts with current state (stale RowVersion, duplicate key). Mapped to 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
