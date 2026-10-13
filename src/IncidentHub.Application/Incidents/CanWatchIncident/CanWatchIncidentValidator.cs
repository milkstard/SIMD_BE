using FluentValidation;

namespace IncidentHub.Application.Incidents.CanWatchIncident;

public sealed class CanWatchIncidentValidator : AbstractValidator<CanWatchIncidentQuery>
{
    public CanWatchIncidentValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
