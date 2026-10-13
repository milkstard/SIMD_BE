using FluentValidation;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.TransitionIncident;

public sealed class TransitionIncidentValidator : AbstractValidator<TransitionIncidentCommand>
{
    public TransitionIncidentValidator()
    {
        // Whether a note, close reason or original incident is required depends on the status: the domain answers that (422).
        RuleFor(x => x.To).IsInEnum();
        RuleFor(x => x.CloseReason).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(Incident.NoteMaxLength);
        RuleFor(x => x.DuplicateOfId).NotEqual(Guid.Empty);
    }
}
