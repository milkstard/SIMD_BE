using FluentValidation;

namespace IncidentHub.Application.Teams.CreateTeam;

public sealed class CreateTeamValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamValidator() => TeamRules.Apply(this, x => x.Name, x => x.Email, x => x.TeamsChannelUrl);
}
