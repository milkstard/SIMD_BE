using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Apps;

public sealed class MonitoredApp : Entity
{
    private MonitoredApp()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public static MonitoredApp Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new MonitoredApp { Name = name };
    }
}
