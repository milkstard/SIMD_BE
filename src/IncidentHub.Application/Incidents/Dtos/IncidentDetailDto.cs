using IncidentHub.Application.Common;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.Dtos;

public sealed record DuplicateOfDto(Guid Id, string Number);

/// <summary>API-CONTRACT §4 <c>IncidentDetail</c>: the summary fields plus the detail-page extras.</summary>
public sealed record IncidentDetailDto(
    Guid Id,
    string Number,
    string Title,
    ApplicationRefDto Application,
    AppEnvironment Environment,
    Severity Severity,
    Priority? Priority,
    IncidentStatus Status,
    UserRefDto? Assignee,
    TeamRefDto? AssignedTeam,
    UserRefDto Reporter,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AckDueAt,
    DateTimeOffset? ResolveDueAt,
    bool IsSlaBreached,
    int CommentCount,
    string RowVersion,
    string Description,
    string? StepsToReproduce,
    IncidentSource Source,
    DuplicateOfDto? DuplicateOf,
    CloseReason? CloseReason,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<IncidentStatus> AllowedTransitions,
    bool CanEdit,
    bool CanAssign) : IVersionedDto;
