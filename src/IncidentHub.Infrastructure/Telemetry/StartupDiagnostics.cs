using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Telemetry;

/// <summary>Emits one log line and one span once the host has started, proving logging and tracing are wired.</summary>
internal sealed class StartupDiagnostics(
    StartupDiagnostics.HostIdentity identity,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILogger<StartupDiagnostics> logger) : IHostedService
{
    private CancellationTokenRegistration _registration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registration = lifetime.ApplicationStarted.Register(OnStarted);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registration.Dispose();
        return Task.CompletedTask;
    }

    private void OnStarted()
    {
        var source = identity.Source;
        using var activity = source.StartActivity($"{source.Name}.startup");
        activity?.SetTag("deployment.environment", environment.EnvironmentName);
        activity?.SetTag("service.version", source.Version);

        logger.LogInformation(
            "{ServiceName} started in {Environment} (version {Version})",
            source.Name,
            environment.EnvironmentName,
            source.Version);
    }

    internal sealed record HostIdentity(ActivitySource Source);
}
