using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Sla.EvaluateSla;
using IncidentHub.Infrastructure.Telemetry;
using MediatR;

namespace IncidentHub.Worker.Jobs;

/// <summary>
/// Every minute, raises the SLA warning and breach of open incidents (<see cref="EvaluateSlaCommand"/>). A Redis lock keeps it
/// to one instance at a time.
/// </summary>
public sealed class SlaMonitor(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider time,
    ILogger<SlaMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public const string LockName = "sla-monitor";

    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "SLA evaluation failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One tick: evaluates unless another instance holds the lock.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.TryAcquireAsync(LockName, LockTtl, cancellationToken);
        if (held is null)
        {
            return;
        }

        using var activity = InfrastructureTelemetry.Source.StartActivity("sla.evaluate");
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new EvaluateSlaCommand(), cancellationToken);

        activity?.SetTag("sla.warned", result.Warned);
        activity?.SetTag("sla.breached", result.Breached);
        activity?.SetTag("sla.failed", result.Failed);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
