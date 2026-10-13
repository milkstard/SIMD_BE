using IncidentHub.Application.Common;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Dashboard.Dtos;

/// <summary>API-CONTRACT §5 <c>AppStatusItem</c>. Contract shape only; produced from BE-11 onwards.</summary>
public sealed record AppStatusItemDto(
    ApplicationRefDto Application,
    AppStatus Status,
    int OpenIncidents,
    Severity? WorstOpenSeverity,
    DateTimeOffset? Since);
