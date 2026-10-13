using FluentValidation;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.FindSimilarIncidents;

public sealed class FindSimilarIncidentsValidator : AbstractValidator<FindSimilarIncidentsQuery>
{
    public FindSimilarIncidentsValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();

        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(t => t.Trim().Length <= Incident.TitleMaxLength)
            .WithMessage($"Title must be at most {Incident.TitleMaxLength} characters.");
    }
}
