using FluentValidation;

namespace IncidentHub.Application.Teams.UpdateTeam;

public sealed class UpdateTeamValidator : AbstractValidator<UpdateTeamCommand>
{
    public UpdateTeamValidator() => TeamRules.Apply(this, x => x.Name, x => x.Email, x => x.TeamsChannelUrl);
}
