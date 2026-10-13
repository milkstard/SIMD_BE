using FluentValidation;
using IncidentHub.Application.Attachments;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.ReportIncident;

public sealed class ReportIncidentValidator : AbstractValidator<ReportIncidentCommand>
{
    public const int TitleMinLength = 5;

    public ReportIncidentValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.Environment).IsInEnum();
        RuleFor(x => x.Severity).IsInEnum();

        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(t => t.Trim().Length is >= TitleMinLength and <= Incident.TitleMaxLength)
            .WithMessage($"Title must be {TitleMinLength}-{Incident.TitleMaxLength} characters.");

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(Incident.DescriptionMaxLength);

        RuleFor(x => x.StepsToReproduce)
            .MaximumLength(Incident.StepsMaxLength);

        RuleFor(x => x.AttachmentIds).MustBeLinkableIds();
    }
}
