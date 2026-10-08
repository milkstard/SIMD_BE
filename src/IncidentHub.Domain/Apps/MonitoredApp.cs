using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Apps;

public sealed class MonitoredApp : Entity
{
    public const int NameMaxLength = 200;
    public const int CodeMaxLength = 20;

    private MonitoredApp()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public Guid OwningTeamId { get; private set; }

    public Guid? EscalationUserId { get; private set; }

    public IReadOnlyList<AppEnvironment> Environments { get; private set; } = [];

    public bool IsActive { get; private set; } = true;

    public byte[] RowVersion { get; private set; } = [];

    public static MonitoredApp Create(
        string name,
        string code,
        Guid owningTeamId,
        Guid? escalationUserId,
        IEnumerable<AppEnvironment> environments)
    {
        var app = new MonitoredApp();
        app.Update(name, code, owningTeamId, escalationUserId, environments, isActive: true);
        return app;
    }

    public void Update(
        string name,
        string code,
        Guid owningTeamId,
        Guid? escalationUserId,
        IEnumerable<AppEnvironment> environments,
        bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, CodeMaxLength);
        if (owningTeamId == Guid.Empty)
        {
            throw new ArgumentException("Owning team is required.", nameof(owningTeamId));
        }

        var envs = environments?.ToList() ?? throw new ArgumentNullException(nameof(environments));
        if (envs.Count == 0)
        {
            throw new ArgumentException("At least one environment is required.", nameof(environments));
        }

        if (envs.Distinct().Count() != envs.Count)
        {
            throw new ArgumentException("Environments must be distinct.", nameof(environments));
        }

        Name = name;
        Code = code.ToUpperInvariant();
        OwningTeamId = owningTeamId;
        EscalationUserId = escalationUserId;
        Environments = envs;
        IsActive = isActive;
    }
}
