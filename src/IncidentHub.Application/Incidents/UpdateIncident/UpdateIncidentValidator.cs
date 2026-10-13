using FluentValidation;
using IncidentHub.Application.Incidents.ReportIncident;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.UpdateIncident;

public sealed class UpdateIncidentValidator : AbstractValidator<UpdateIncidentCommand>
{
    public UpdateIncidentValidator()
    {
        RuleFor(x => x)
            .Must(x => x.HasAnyField)
            .OverridePropertyName("body")
            .WithMessage("At least one field must be provided.");

        When(x => x.Title.HasValue, () =>
            RuleFor(x => x.Title.Value)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(t => t!.Trim().Length is >= ReportIncidentValidator.TitleMinLength and <= Incident.TitleMaxLength)
                .WithMessage($"Title must be {ReportIncidentValidator.TitleMinLength}-{Incident.TitleMaxLength} characters.")
                .OverridePropertyName("title"));

        When(x => x.Description.HasValue, () =>
            RuleFor(x => x.Description.Value)
                .NotEmpty()
                .MaximumLength(Incident.DescriptionMaxLength)
                .OverridePropertyName("description"));

        // Null clears the steps.
        When(x => x.StepsToReproduce.HasValue, () =>
            RuleFor(x => x.StepsToReproduce.Value)
                .MaximumLength(Incident.StepsMaxLength)
                .OverridePropertyName("stepsToReproduce"));

        When(x => x.Severity.HasValue, () =>
        {
            RuleFor(x => x.Severity.Value)
                .NotNull()
                .Must(s => s is null || Enum.IsDefined(s.Value))
                .OverridePropertyName("severity");

            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("A reason is required when the severity changes.")
                .MaximumLength(Incident.NoteMaxLength)
                .OverridePropertyName("reason");
        });

        // Null clears the priority.
        When(x => x.Priority.HasValue, () =>
            RuleFor(x => x.Priority.Value)
                .Must(p => p is null || Enum.IsDefined(p.Value))
                .OverridePropertyName("priority"));

        When(x => x.ApplicationId.HasValue, () =>
            RuleFor(x => x.ApplicationId.Value)
                .NotNull()
                .Must(id => id is null || id.Value != Guid.Empty)
                .OverridePropertyName("applicationId"));

        When(x => x.Environment.HasValue, () =>
            RuleFor(x => x.Environment.Value)
                .NotNull()
                .Must(e => e is null || Enum.IsDefined(e.Value))
                .OverridePropertyName("environment"));
    }
}
