using System.Diagnostics;

namespace IncidentHub.Infrastructure.Telemetry;

public static class InfrastructureTelemetry
{
    public const string SourceName = "IncidentHub.Infrastructure";

    public static readonly ActivitySource Source = new(SourceName);
}
