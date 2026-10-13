using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using MediatR;

namespace IncidentHub.Application.Incidents.UpdateIncident;

/// <summary>
/// A partial edit. Every field is an <see cref="Optional{T}"/> of a nullable value, so a field that was sent as an explicit
/// <c>null</c> reaches the validator (422 on that field) and is not mistaken for "not sent".
/// </summary>
/// <param name="Reason">Required when <paramref name="Severity"/> is present; ignored otherwise.</param>
/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record UpdateIncidentCommand(
    Guid IncidentId,
    Optional<string?> Title,
    Optional<string?> Description,
    Optional<string?> StepsToReproduce,
    Optional<Severity?> Severity,
    Optional<Priority?> Priority,
    Optional<Guid?> ApplicationId,
    Optional<AppEnvironment?> Environment,
    string? Reason,
    byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>
{
    public bool HasAnyField =>
        Title.HasValue || Description.HasValue || StepsToReproduce.HasValue || Severity.HasValue
        || Priority.HasValue || ApplicationId.HasValue || Environment.HasValue;

    /// <summary>Severity, priority, application and environment are responder-level fields.</summary>
    public bool HasResponderField => Severity.HasValue || Priority.HasValue || ApplicationId.HasValue || Environment.HasValue;

    /// <summary>Names of the fields that were sent, for the log (never their values).</summary>
    public IReadOnlyList<string> SentFields()
    {
        var fields = new List<string>();
        AddIf(Title.HasValue, nameof(Title));
        AddIf(Description.HasValue, nameof(Description));
        AddIf(StepsToReproduce.HasValue, nameof(StepsToReproduce));
        AddIf(Severity.HasValue, nameof(Severity));
        AddIf(Priority.HasValue, nameof(Priority));
        AddIf(ApplicationId.HasValue, nameof(ApplicationId));
        AddIf(Environment.HasValue, nameof(Environment));
        return fields;

        void AddIf(bool present, string name)
        {
            if (present)
            {
                fields.Add(name);
            }
        }
    }
}
