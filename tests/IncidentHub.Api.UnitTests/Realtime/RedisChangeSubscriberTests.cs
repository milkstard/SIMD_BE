using System.Text.Json;
using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;

namespace IncidentHub.Api.UnitTests.Realtime;

public sealed class RedisChangeSubscriberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IPostCommitDispatcher _dispatcher = Substitute.For<IPostCommitDispatcher>();
    private readonly ProcessOrigin _origin = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeLogger<RedisChangeSubscriber> _logger = new();
    private readonly List<IReadOnlyList<IDomainEvent>> _dispatched = [];

    public RedisChangeSubscriberTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        ClaimResult(true);
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _dispatched.Add(call.Arg<IReadOnlyList<IDomainEvent>>());
                return Task.CompletedTask;
            });
    }

    private RedisChangeSubscriber Sut => new(_redis, _dispatcher, _origin, _time, _logger);

    private void ClaimResult(bool won) =>
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists).Returns(won);

    private static string Message(Guid origin, Guid? incidentId = null, Guid? previousApplication = null) =>
        JsonSerializer.Serialize(
            new IncidentChangeMessage(Guid.NewGuid(), origin, incidentId ?? Guid.NewGuid(), ["IsSlaBreached"], previousApplication, Now),
            OutboxJson.Options);

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"incidentId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task Handle_MalformedMessage_IsIgnoredAndLogged(string payload)
    {
        await Sut.HandleAsync(payload, CancellationToken.None);

        _dispatched.Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning && r.Message.Contains("malformed"));
    }

    [Fact]
    public async Task Handle_OwnOrigin_IsIgnored()
    {
        await Sut.HandleAsync(Message(_origin.Id), CancellationToken.None);

        _dispatched.Should().BeEmpty();
        await _db.DidNotReceiveWithAnyArgs().StringSetAsync(default, default, default, default(When));
    }

    [Fact]
    public async Task Handle_ClaimLostToAnotherInstance_DoesNotDispatch()
    {
        ClaimResult(false);

        await Sut.HandleAsync(Message(Guid.NewGuid()), CancellationToken.None);

        _dispatched.Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ClaimFails_DoesNotDispatchAndLogs()
    {
        var id = Guid.NewGuid();
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists)
            .Returns<Task<bool>>(_ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

        var handle = () => Sut.HandleAsync(Message(Guid.NewGuid(), id), CancellationToken.None);

        await handle.Should().NotThrowAsync();
        _dispatched.Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning && r.Message.Contains(id.ToString()));
    }

    [Fact]
    public async Task Handle_ValidMessage_DispatchesTheIncidentUpdatedEvent()
    {
        var id = Guid.NewGuid();
        var previousApplication = Guid.NewGuid();

        await Sut.HandleAsync(Message(Guid.NewGuid(), id, previousApplication), CancellationToken.None);

        var updated = _dispatched.Should().ContainSingle().Subject.Should().ContainSingle().Subject.Should().BeOfType<IncidentUpdated>().Subject;
        updated.IncidentId.Should().Be(id);
        updated.ChangedFields.Should().Equal("IsSlaBreached");
        updated.PreviousApplicationId.Should().Be(previousApplication);
        updated.OccurredAt.Should().Be(Now);
        updated.ActorId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidMessage_ClaimsItWithAnExpiryBeforeDispatching()
    {
        var message = JsonSerializer.Deserialize<IncidentChangeMessage>(Message(Guid.NewGuid()), OutboxJson.Options)!;

        await Sut.HandleAsync(JsonSerializer.Serialize(message, OutboxJson.Options), CancellationToken.None);

        await _db.Received(1).StringSetAsync(
            (RedisKey)$"changes:claim:{message.MessageId:N}", Arg.Any<RedisValue>(), ChangeChannel.ClaimTtl, When.NotExists);
    }

    [Fact]
    public async Task Handle_DispatcherThrows_LogsAndDoesNotThrow()
    {
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("hub down")));

        var handle = () => Sut.HandleAsync(Message(Guid.NewGuid()), CancellationToken.None);

        await handle.Should().NotThrowAsync();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning && r.Message.StartsWith("Relay of the change"));
    }

    [Fact]
    public async Task Handle_DispatchNeverCompletes_GivesUpAtTheCap()
    {
        CancellationToken seen = default;
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Do<CancellationToken>(t => seen = t))
            .Returns(_ => new TaskCompletionSource().Task);

        var handle = Sut.HandleAsync(Message(Guid.NewGuid()), CancellationToken.None);
        handle.IsCompleted.Should().BeFalse();

        _time.Advance(PostCommitHook.Timeout);
        await handle.WaitAsync(TimeSpan.FromSeconds(5));

        seen.IsCancellationRequested.Should().BeTrue();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_HostStopping_Rethrows()
    {
        using var stopping = new CancellationTokenSource();
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new TaskCompletionSource().Task);

        var handle = Sut.HandleAsync(Message(Guid.NewGuid()), stopping.Token);
        await stopping.CancelAsync();

        await handle.Invoking(h => h).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(typeof(RedisConnectionException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Execute_SubscribeFails_LogsAndResubscribesAfterTheDelay(Type exceptionType)
    {
        var calls = 0;
        _redis.GetSubscriber(Arg.Any<object>()).Returns(_ =>
        {
            Interlocked.Increment(ref calls);
            throw exceptionType == typeof(RedisConnectionException)
                ? new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")
                : new InvalidOperationException("boom");
        });
        var sut = Sut;

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
        await WaitUntilAsync(() => _logger.Collector.GetSnapshot().Any(r => r.Level == LogLevel.Warning));
        _time.Advance(RedisChangeSubscriber.ResubscribeDelay);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
        await sut.StopAsync(CancellationToken.None);

        Volatile.Read(ref calls).Should().BeGreaterThanOrEqualTo(2, "a failure of any type must not end the loop or stop the host");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in 5 s.");
            }

            await Task.Delay(10);
        }
    }
}
