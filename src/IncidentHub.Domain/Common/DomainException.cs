namespace IncidentHub.Domain.Common;

/// <summary>A business rule was broken. The Api maps each subclass to a ProblemDetails response.</summary>
public abstract class DomainException(string message) : Exception(message);
