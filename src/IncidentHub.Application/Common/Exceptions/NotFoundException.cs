namespace IncidentHub.Application.Common.Exceptions;

/// <summary>The requested resource does not exist or is not visible to the caller. Mapped to 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);
