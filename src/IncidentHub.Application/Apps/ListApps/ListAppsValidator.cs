using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.Apps.ListApps;

public sealed class ListAppsValidator : AbstractValidator<ListAppsQuery>
{
    public ListAppsValidator() =>
        RuleFor(x => x.Limit).InclusiveBetween(1, PageLimits.Max);
}
