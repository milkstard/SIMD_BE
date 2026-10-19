using FluentValidation;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.Sla.UpdateSlaPolicy;

public sealed class UpdateSlaPolicyValidator : AbstractValidator<UpdateSlaPolicyCommand>
{
    public UpdateSlaPolicyValidator()
    {
        RuleFor(x => x.AckMinutes).GreaterThan(0);

        RuleFor(x => x.ResolveMinutes)
            .GreaterThanOrEqualTo(x => x.AckMinutes)
            .WithMessage("'Resolve Minutes' must be greater than or equal to 'Ack Minutes'.")
            .LessThanOrEqualTo(SlaPolicySetting.MaxResolveMinutes);

        RuleFor(x => x.EscalateAtPercent)
            .InclusiveBetween(SlaPolicy.MinEscalatePercent, SlaPolicy.MaxEscalatePercent);
    }
}
