using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using MediatR;

namespace IncidentHub.Application.Incidents.ReportIncident;

public sealed record ReportIncidentCommand(
    Guid ApplicationId,
    AppEnvironment Environment,
    Severity Severity,
    string Title,
    string Description,
    string? StepsToReproduce,
    IReadOnlyList<Guid> AttachmentIds) : IRequest<IncidentDetailDto>;
