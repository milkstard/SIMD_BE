using IncidentHub.Application.Common;

namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>API-CONTRACT §4 <c>Comment</c>.</summary>
public sealed record CommentDto(
    Guid Id,
    Guid IncidentId,
    UserRefDto Author,
    string Body,
    bool IsInternal,
    IReadOnlyList<AttachmentDto> Attachments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);
