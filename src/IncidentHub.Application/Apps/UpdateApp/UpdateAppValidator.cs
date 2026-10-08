using FluentValidation;

namespace IncidentHub.Application.Apps.UpdateApp;

public sealed class UpdateAppValidator : AbstractValidator<UpdateAppCommand>
{
    public UpdateAppValidator() =>
        AppRules.Apply(this, x => x.Name, x => x.Code, x => x.OwningTeamId, x => x.EscalationContactId, x => x.Environments);
}
