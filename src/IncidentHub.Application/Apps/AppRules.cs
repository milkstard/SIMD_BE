using FluentValidation;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Application.Apps;

/// <summary>Field rules shared by the create and update validators.</summary>
internal static class AppRules
{
    public const string CodePattern = "^[A-Za-z0-9_-]{2,20}$";

    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, string?> name,
        Func<T, string?> code,
        Func<T, Guid> owningTeamId,
        Func<T, Guid?> escalationContactId,
        Func<T, IReadOnlyList<AppEnvironment>?> environments)
    {
        validator.RuleFor(x => name(x))
            .NotEmpty().MaximumLength(MonitoredApp.NameMaxLength)
            .OverridePropertyName("Name");

        validator.RuleFor(x => code(x))
            .NotEmpty().Matches(CodePattern)
            .WithMessage("Code must be 2-20 characters: letters, digits, underscore or hyphen.")
            .OverridePropertyName("Code");

        validator.RuleFor(x => owningTeamId(x))
            .NotEmpty()
            .OverridePropertyName("OwningTeamId");

        validator.RuleFor(x => escalationContactId(x))
            .NotEqual(Guid.Empty)
            .When(x => escalationContactId(x) is not null)
            .OverridePropertyName("EscalationContactId");

        validator.RuleFor(x => environments(x))
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(list => list!.Distinct().Count() == list!.Count)
            .WithMessage("Environments must be distinct.")
            .Must(list => list!.All(Enum.IsDefined))
            .WithMessage("Environments contains an unknown value.")
            .OverridePropertyName("Environments");
    }
}
