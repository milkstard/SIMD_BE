using FluentValidation;

namespace IncidentHub.Application.Incidents.AssignIncident;

public sealed class AssignIncidentValidator : AbstractValidator<AssignIncidentCommand>
{
    public AssignIncidentValidator()
    {
        RuleFor(x => x.AssigneeId).NotEqual(Guid.Empty);
        RuleFor(x => x.TeamId).NotEqual(Guid.Empty);
    }
}
