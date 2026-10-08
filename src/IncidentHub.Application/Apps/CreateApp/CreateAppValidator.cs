using FluentValidation;

namespace IncidentHub.Application.Apps.CreateApp;

public sealed class CreateAppValidator : AbstractValidator<CreateAppCommand>
{
    public CreateAppValidator() =>
        AppRules.Apply(this, x => x.Name, x => x.Code, x => x.OwningTeamId, x => x.EscalationContactId, x => x.Environments);
}
