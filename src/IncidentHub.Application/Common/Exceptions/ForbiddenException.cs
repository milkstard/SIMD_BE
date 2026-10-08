namespace IncidentHub.Application.Common.Exceptions;

/// <summary>The caller may see the resource but is not allowed to perform this change. Mapped to 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
