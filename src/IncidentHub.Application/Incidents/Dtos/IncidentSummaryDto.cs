using IncidentHub.Application.Common;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>API-CONTRACT §4 <c>IncidentSummary</c>: lists, dashboard feed, SignalR.</summary>
public sealed record IncidentSummaryDto(
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
    string RowVersion) : IVersionedDto;
