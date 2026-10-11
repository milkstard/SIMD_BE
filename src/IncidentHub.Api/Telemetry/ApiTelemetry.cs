using System.Diagnostics;
using System.Reflection;

namespace IncidentHub.Api.Telemetry;

public static class ApiTelemetry
{
    public const string SourceName = "IncidentHub.Api";

    private const string AccessTokenParameter = "access_token";

    public static readonly ActivitySource Source = new(
        SourceName,
        typeof(ApiTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// SignalR clients send the JWT as <c>?access_token=</c> on <c>/hubs/*</c>; never let it reach an exported span.
    /// </summary>
    public static void RedactAccessToken(Activity activity, HttpRequest request)
    {
        if (request.Query.ContainsKey(AccessTokenParameter))
        {
            activity.SetTag("url.query", $"{AccessTokenParameter}=Redacted");
        }
    }
}
