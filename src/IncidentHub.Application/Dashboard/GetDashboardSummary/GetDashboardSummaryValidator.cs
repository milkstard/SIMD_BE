using FluentValidation;

namespace IncidentHub.Application.Dashboard.GetDashboardSummary;

public sealed class GetDashboardSummaryValidator : AbstractValidator<GetDashboardSummaryQuery>
{
    public GetDashboardSummaryValidator()
    {
        RuleFor(x => x.Range)
            .Must(v => DashboardRanges.TryParse(v, out _))
            .WithMessage("Range must be '24h', '7d' or '30d'.");
    }
}
