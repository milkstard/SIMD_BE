using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.Incidents.ListIncidents;

public sealed class ListIncidentsValidator : AbstractValidator<ListIncidentsQuery>
{
    public ListIncidentsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, PageLimits.Max);
        RuleFor(x => x.Q).MaximumLength(ListIncidentsQuery.MaxQueryLength);
        RuleForEach(x => x.Status).IsInEnum();
        RuleForEach(x => x.Severity).IsInEnum();
        RuleFor(x => x.Environment).IsInEnum();
        RuleFor(x => x.Sort).IsInEnum();

        RuleFor(x => x.CreatedTo)
            .GreaterThanOrEqualTo(x => x.CreatedFrom!.Value)
            .When(x => x.CreatedFrom is not null && x.CreatedTo is not null)
            .WithMessage("CreatedTo must not be before CreatedFrom.");

        RuleFor(x => x.AssigneeId)
            .Must(v => IsToken(v, ListIncidentsQuery.Me, ListIncidentsQuery.Unassigned))
            .WithMessage("AssigneeId must be a GUID, 'me' or 'unassigned'.");

        RuleFor(x => x.ReporterId)
            .Must(v => IsToken(v, ListIncidentsQuery.Me))
            .WithMessage("ReporterId must be a GUID or 'me'.");

        RuleFor(x => x.Cursor)
            .Must((query, cursor) => IncidentCursor.TryDecode(cursor, query.Sort, out _))
            .WithMessage("Cursor is invalid.");
    }

    private static bool IsToken(string? value, params string[] tokens) =>
        value is null
        || tokens.Contains(value, StringComparer.OrdinalIgnoreCase)
        || (Guid.TryParse(value, out var id) && id != Guid.Empty);
}
