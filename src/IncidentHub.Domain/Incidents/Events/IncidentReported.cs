using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

public sealed record IncidentReported(
    Guid IncidentId,
    Guid ApplicationId,
    AppEnvironment Environment,
    Severity Severity,
    IncidentSource Source,
    Guid ReporterId,
    DateTimeOffset OccurredAt) : IDomainEvent;
