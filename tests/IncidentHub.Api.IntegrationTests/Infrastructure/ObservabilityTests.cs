using System.Diagnostics;
using FluentAssertions;
using IncidentHub.Api.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Core;
using Serilog.Events;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ObservabilityTests(ApiFactory factory)
{
    [Fact]
    public async Task Startup_WhenHostStarts_EmitsStartupLogAndSpan()
    {
        var (activities, logs) = (new List<Activity>(), new CollectingSink());
        await using var host = CreateObservedFactory(activities, logs);

        host.CreateClient();

        activities.Should().ContainSingle(a => a.OperationName == $"{ApiTelemetry.SourceName}.startup")
            .Which.GetTagItem("deployment.environment").Should().Be("Development");
        logs.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "{ServiceName} started in {Environment} (version {Version})")
            .Which.Properties["ServiceName"].ToString().Should().Contain(ApiTelemetry.SourceName);
    }

    [Fact]
    public async Task HubRequest_WithAccessTokenQuery_RedactsUrlQueryTag()
    {
        var activities = new List<Activity>();
        await using var host = CreateObservedFactory(activities, new CollectingSink());
        const string secret = "not-a-real-token-but-secret";

        await host.CreateClient().PostAsync($"/hubs/incidents/negotiate?negotiateVersion=1&access_token={secret}", null);

        var request = await WaitForAsync(activities, a => a.GetTagItem("url.path") as string == "/hubs/incidents/negotiate");
        request.TagObjects.Should().NotContain(tag => Convert.ToString(tag.Value)!.Contains(secret));
        activities.SelectMany(a => a.TagObjects).Should().NotContain(tag => Convert.ToString(tag.Value)!.Contains(secret));
    }

    private WebApplicationFactory<Program> CreateObservedFactory(List<Activity> activities, CollectingSink logs) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(activities));
            services.AddSingleton<ILogEventSink>(logs);
        }));

    // The server span is exported when the request pipeline finishes, which can be just after the client sees the response.
    private static async Task<Activity> WaitForAsync(List<Activity> activities, Func<Activity, bool> match)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            lock (activities)
            {
                var found = activities.FirstOrDefault(match);
                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("Expected activity was not exported.");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
