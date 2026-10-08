using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Teams;

public sealed class Team : Entity
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int ChannelUrlMaxLength = 500;

    private Team()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? TeamsChannelUrl { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Team Create(string name, string email, string? teamsChannelUrl)
    {
        var team = new Team();
        team.Update(name, email, teamsChannelUrl);
        return team;
    }

    public void Update(string name, string email, string? teamsChannelUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(email.Length, EmailMaxLength);

        if (teamsChannelUrl is not null)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(teamsChannelUrl.Length, ChannelUrlMaxLength);
            if (!Uri.TryCreate(teamsChannelUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Channel URL must be an absolute https URL.", nameof(teamsChannelUrl));
            }
        }

        Name = name;
        Email = email;
        TeamsChannelUrl = teamsChannelUrl;
    }
}
