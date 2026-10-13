using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Attachments;

/// <summary>The attachment is not <c>Clean</c> (still scanning, or infected), so it cannot be downloaded. Mapped to 409.</summary>
public sealed class AttachmentNotReadyException(ScanStatus status)
    : Exception($"Attachment is {status} and cannot be downloaded.");
