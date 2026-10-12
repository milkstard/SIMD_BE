using IncidentHub.Application.Common;

namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>Contract shape only; attachments are linked from BE-08 onwards.</summary>
public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    UserRefDto UploadedBy,
    DateTimeOffset UploadedAt);
