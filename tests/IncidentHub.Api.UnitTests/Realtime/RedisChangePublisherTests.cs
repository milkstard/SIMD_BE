using System.Text.Json;
using FluentAssertions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using StackExchange.Redis;

namespace IncidentHub.Api.UnitTests.Realtime;

public sealed class RedisChangePublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();
    private readonly ProcessOrigin _origin = new();
    private readonly FakeLogger<RedisChangePublisher> _logger = new();
    private readonly List<(string Channel, string Payload)> _published = [];

    public RedisChangePublisherTests()
    {
        _redis.GetSubscriber(Arg.Any<object>()).Returns(_subscriber);
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                _published.Add((call.Arg<RedisChannel>().ToString(), (string)call.Arg<RedisValue>()!));
                return Task.FromResult(1L);
            });
    }

    private RedisChangePublisher Sut => new(_redis, _origin, _logger);

    private static IncidentUpdated Breach(Guid id) => new(id, ["IsSlaBreached"], null, Now);

    [Fact]
    public async Task Dispatch_IncidentUpdated_PublishesIdsAndFieldNamesOnly()
    {
        var id = Guid.NewGuid();
        var previousApplication = Guid.NewGuid();

        await Sut.DispatchAsync([new IncidentUpdated(id, ["IsSlaBreached"], Guid.NewGuid(), Now, previousApplication)], CancellationToken.None);

        var (channel, payload) = _published.Should().ContainSingle().Subject;
        channel.Should().Be("incidenthub:changes");
        var message = JsonSerializer.Deserialize<IncidentChangeMessage>(payload, OutboxJson.Options)!;
        message.IncidentId.Should().Be(id);
        message.ChangedFields.Should().Equal("IsSlaBreached");
        message.PreviousApplicationId.Should().Be(previousApplication);
        message.OccurredAt.Should().Be(Now);
        message.Origin.Should().Be(_origin.Id);
        message.MessageId.Should().NotBeEmpty();
        JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("messageId", "origin", "incidentId", "changedFields", "previousApplicationId", "occurredAt");
    }

    [Fact]
    public async Task Dispatch_TwoUpdates_PublishesTwoMessagesWithDistinctIds()
    {
        await Sut.DispatchAsync([Breach(Guid.NewGuid()), Breach(Guid.NewGuid())], CancellationToken.None);

        var ids = _published
            .Select(p => JsonSerializer.Deserialize<IncidentChangeMessage>(p.Payload, OutboxJson.Options)!.MessageId)
            .ToList();
        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Dispatch_EventsOtherThanIncidentUpdated_PublishNothing()
    {
        var id = Guid.NewGuid();

        await Sut.DispatchAsync([new SlaBreached(id, Now), new SlaWarning(id, Now)], CancellationToken.None);

        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_RedisConnectionFails_LogsAndDoesNotThrow()
    {
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns<Task<long>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var id = Guid.NewGuid();

        var dispatch = () => Sut.DispatchAsync([Breach(id)], CancellationToken.None);

        await dispatch.Should().NotThrowAsync();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning && r.Message.Contains(id.ToString()));
    }

    [Fact]
    public async Task Dispatch_RedisTimesOut_LogsAndStillTriesTheNextMessage()
    {
        var calls = 0;
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(_ => ++calls == 1
                ? Task.FromException<long>(new RedisTimeoutException("slow", CommandStatus.Unknown))
                : Task.FromResult(1L));

        var dispatch = () => Sut.DispatchAsync([Breach(Guid.NewGuid()), Breach(Guid.NewGuid())], CancellationToken.None);

        await dispatch.Should().NotThrowAsync();
        calls.Should().Be(2);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning);
    }
}
