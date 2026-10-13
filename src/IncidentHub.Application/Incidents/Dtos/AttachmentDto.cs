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
    DateTimeOffset UploadedAt)
{
    public static AttachmentDto From(Attachment attachment, UserRefDto uploadedBy) =>
        new(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.ScanStatus,
            uploadedBy,
            attachment.CreatedAt);
}
