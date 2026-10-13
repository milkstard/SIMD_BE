using IncidentHub.Application.Common;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>API-CONTRACT Attachment. BlobPath is deliberately absent.</summary>
public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    ScanStatus ScanStatus,
    UserRefDto UploadedBy,
    DateTimeOffset UploadedAt);
