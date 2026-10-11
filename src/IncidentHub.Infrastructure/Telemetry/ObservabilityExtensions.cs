using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace IncidentHub.Infrastructure.Telemetry;

public static class ObservabilityExtensions
{
    /// <summary>Standard OpenTelemetry variable; exporters are only added when it is set, so tests and CI stay quiet.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private const string OtlpProtocolKey = "OTEL_EXPORTER_OTLP_PROTOCOL";

    /// <summary>
    /// Wires Serilog and OpenTelemetry for a host. <paramref name="hostSource"/> names the service (its name and version
    /// become <c>service.name</c>/<c>service.version</c>) and carries the startup span.
    /// </summary>
    public static IHostApplicationBuilder AddIncidentHubObservability(
        this IHostApplicationBuilder builder,
        ActivitySource hostSource,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var serviceName = hostSource.Name;
        var serviceVersion = hostSource.Version ?? "0.0.0";

        // Shared by the Serilog OTLP sink and the OTel SDK so a backend groups this process's logs and traces together.
        var serviceInstanceId = Guid.NewGuid().ToString();
        var otlpEndpoint = builder.Configuration[OtlpEndpointKey];
        var useHttp = string.Equals(builder.Configuration[OtlpProtocolKey], "http/protobuf", StringComparison.OrdinalIgnoreCase);

        // AddSerilog (not a static bootstrap logger) so WebApplicationFactory and `dotnet swagger tofile` can build the
        // host repeatedly. ReadFrom.Services picks up ILogEventSink registrations (used by tests).
        builder.Services.AddSerilog((services, logger) =>
        {
            logger.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", serviceName)
                .WriteTo.Console();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = useHttp ? $"{otlpEndpoint.TrimEnd('/')}/v1/logs" : otlpEndpoint;
                    options.Protocol = useHttp ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["service.version"] = serviceVersion,
                        ["service.instance.id"] = serviceInstanceId,
                    };
                });
            }
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: serviceInstanceId))
            .WithTracing(tracing =>
            {
                tracing.AddSource(serviceName, InfrastructureTelemetry.SourceName)
                    .AddHttpClientInstrumentation();
                configureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddHttpClientInstrumentation();
                configureMetrics?.Invoke(metrics);
            });

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otel.UseOtlpExporter();
        }

        builder.Services.AddSingleton(new StartupDiagnostics.HostIdentity(hostSource));
        builder.Services.AddHostedService<StartupDiagnostics>();

        return builder;
    }
}
