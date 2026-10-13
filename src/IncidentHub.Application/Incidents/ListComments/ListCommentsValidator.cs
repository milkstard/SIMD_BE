using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.Incidents.ListComments;

public sealed class ListCommentsValidator : AbstractValidator<ListCommentsQuery>
{
    public ListCommentsValidator() =>
        RuleFor(x => x.Limit).InclusiveBetween(1, PageLimits.Max);
}
