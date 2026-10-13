namespace IncidentHub.Application.Common.Exceptions;

/// <summary>An uploaded file is larger than allowed. Mapped to 413.</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);
