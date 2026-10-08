using FluentValidation;
using IncidentHub.Domain.Teams;

namespace IncidentHub.Application.Teams;

/// <summary>Field rules shared by the create and update validators.</summary>
internal static class TeamRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, string?> name,
        Func<T, string?> email,
        Func<T, string?> channelUrl)
    {
        validator.RuleFor(x => name(x))
            .NotEmpty().MaximumLength(Team.NameMaxLength)
            .OverridePropertyName("Name");

        validator.RuleFor(x => email(x))
            .NotEmpty().EmailAddress().MaximumLength(Team.EmailMaxLength)
            .OverridePropertyName("Email");

        validator.RuleFor(x => channelUrl(x))
            .MaximumLength(Team.ChannelUrlMaxLength)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Teams channel URL must be an absolute https URL.")
            .When(x => channelUrl(x) is not null)
            .OverridePropertyName("TeamsChannelUrl");
    }
}
