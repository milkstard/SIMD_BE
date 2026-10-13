using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.Incidents.ListHistory;

public sealed class ListHistoryValidator : AbstractValidator<ListHistoryQuery>
{
    public ListHistoryValidator() =>
        RuleFor(x => x.Limit).InclusiveBetween(1, PageLimits.Max);
}
