using System.Net;
using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Notifications.Chat;
using IncidentHub.Infrastructure.Notifications.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.Registry;
using static IncidentHub.Api.UnitTests.Notifications.ChatTestKit;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class ChatResilienceTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeWebhookHandler _handler = new();

    private static NotificationResilienceOptions Options(int retries = 3, int throughput = 5) => new()
    {
        MaxRetries = retries,
        RetryBaseDelay = TimeSpan.FromSeconds(1),
        AttemptTimeout = TimeSpan.FromSeconds(10),
        TotalTimeout = TimeSpan.FromSeconds(45),
        MinimumThroughput = throughput,
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30),
    };

    private ResiliencePipeline Pipeline(NotificationResilienceOptions options)
    {
        var builder = new ResiliencePipelineBuilder { TimeProvider = _time };
        NotificationResilience.Configure(builder, options);
        return builder.Build();
    }

    private SlackNotificationSender Slack(ResiliencePipeline pipeline) =>
        new(new StubHttpClientFactory(_handler), pipeline, Microsoft.Extensions.Options.Options.Create(new ChatOptions()), new FakeLogger<SlackNotificationSender>());

    /// <summary>Runs the action while advancing the fake clock, so retry delays and timeouts elapse.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        var task = action();
        for (var i = 0; i < 300 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        await task;
    }

    [Fact]
    public async Task Pipeline_TransientFailure_RetriesThenSucceeds()
    {
        var calls = 0;
        _handler.Respond = _ => Task.FromResult(new HttpResponseMessage(++calls < 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));
        var sender = Slack(Pipeline(Options()));

        await RunAsync(() => sender.SendAsync(Message(SlackUrl), CancellationToken.None));

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_AlwaysFailing_StopsAfterMaxRetriesAndSurfacesDeliveryException()
    {
        var options = Options(retries: 2, throughput: 100);
        _handler.Always(HttpStatusCode.InternalServerError);
        var sender = Slack(Pipeline(options));

        var act = () => RunAsync(() => sender.SendAsync(Message(SlackUrl), CancellationToken.None));

        await act.Should().ThrowAsync<NotificationDeliveryException>();
        _handler.Requests.Should().HaveCount(3, "one try and two retries");
    }

    [Fact]
    public async Task Pipeline_NonRetryable_NotRetried()
    {
        _handler.Always(HttpStatusCode.NotFound);
        var sender = Slack(Pipeline(Options()));

        var act = () => sender.SendAsync(Message(SlackUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        _handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Pipeline_SlackFailures_DoNotOpenTeamsCircuit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Chat:Enabled"] = "true",
            ["Notifications:Resilience:MaxRetries"] = "0",
            ["Notifications:Resilience:MinimumThroughput"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationSenders(configuration);
        await using var provider = services.BuildServiceProvider();
        var pipelines = provider.GetRequiredService<ResiliencePipelineProvider<string>>();
        var chatOptions = provider.GetRequiredService<IOptions<ChatOptions>>();
        var factory = new StubHttpClientFactory(_handler);
        var slack = new SlackNotificationSender(factory, pipelines.GetPipeline(NotificationResilience.SlackPipeline), chatOptions, new FakeLogger<SlackNotificationSender>());
        var teams = new TeamsNotificationSender(factory, pipelines.GetPipeline(NotificationResilience.TeamsPipeline), chatOptions, new FakeLogger<TeamsNotificationSender>());

        _handler.Always(HttpStatusCode.InternalServerError);
        for (var i = 0; i < 2; i++)
        {
            await FluentActions.Awaiting(() => slack.SendAsync(Message(SlackUrl), CancellationToken.None))
                .Should().ThrowAsync<NotificationDeliveryException>();
        }

        var callsBefore = _handler.Requests.Count;
        var slackAgain = await FluentActions.Awaiting(() => slack.SendAsync(Message(SlackUrl), CancellationToken.None))
            .Should().ThrowAsync<NotificationDeliveryException>();
        slackAgain.Which.Message.Should().Contain("circuit is open");
        _handler.Requests.Should().HaveCount(callsBefore, "an open circuit rejects without calling");

        _handler.Always(HttpStatusCode.OK);
        await teams.SendAsync(Message(TeamsUrl), CancellationToken.None);
        _handler.Requests.Should().HaveCount(callsBefore + 1, "the teams circuit is still closed");
    }

    [Fact]
    public async Task Pipeline_AfterBreak_HalfOpensAndCloses()
    {
        var options = Options(retries: 0, throughput: 2);
        var sender = Slack(Pipeline(options));
        _handler.Always(HttpStatusCode.InternalServerError);

        for (var i = 0; i < 2; i++)
        {
            await FluentActions.Awaiting(() => sender.SendAsync(Message(SlackUrl), CancellationToken.None))
                .Should().ThrowAsync<NotificationDeliveryException>();
        }

        _time.Advance(options.BreakDuration + TimeSpan.FromSeconds(1));
        _handler.Always(HttpStatusCode.OK);

        for (var i = 0; i < 3; i++)
        {
            await sender.SendAsync(Message(SlackUrl), CancellationToken.None);
        }

        _handler.Requests.Should().HaveCount(5, "the trial call succeeds and the circuit closes again");
    }

    [Fact]
    public async Task Pipeline_SlowPost_TimesOut()
    {
        _handler.Hang = true;
        var sender = Slack(Pipeline(Options(retries: 0)));

        var act = () => RunAsync(() => sender.SendAsync(Message(SlackUrl), CancellationToken.None));

        var thrown = await act.Should().ThrowAsync<NotificationDeliveryException>();
        thrown.Which.Message.Should().Contain("timed out");
    }
}
