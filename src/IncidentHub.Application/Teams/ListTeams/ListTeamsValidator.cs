using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.Teams.ListTeams;

public sealed class ListTeamsValidator : AbstractValidator<ListTeamsQuery>
{
    public ListTeamsValidator() =>
        RuleFor(x => x.Limit).InclusiveBetween(1, PageLimits.Max);
}
