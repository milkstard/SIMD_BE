using System.Diagnostics;
using System.Reflection;

namespace IncidentHub.Worker.Telemetry;

public static class WorkerTelemetry
{
    public const string SourceName = "IncidentHub.Worker";

    public static readonly ActivitySource Source = new(
        SourceName,
        typeof(WorkerTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
