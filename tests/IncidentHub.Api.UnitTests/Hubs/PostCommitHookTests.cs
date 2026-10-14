using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Hubs;

public sealed class PostCommitHookTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeLogger<PostCommitHook> _logger = new();
    private readonly IPostCommitDispatcher _dispatcher = Substitute.For<IPostCommitDispatcher>();

    private PostCommitHook Sut => new(_dispatcher, _time, _logger);

    private static IDomainEvent[] OneEvent() => [new IncidentUpdated(Guid.NewGuid(), ["Title"], null, Now)];

    [Fact]
    public async Task Run_NoEvents_DoesNotCallDispatcher()
    {
        await Sut.RunAsync([]);

        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task Run_DispatcherNeverCompletes_ReturnsAtTheCapCancelsItAndLogs()
    {
        CancellationToken seen = default;
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Do<CancellationToken>(t => seen = t))
            .Returns(_ => new TaskCompletionSource().Task);

        var run = Sut.RunAsync(OneEvent());
        run.IsCompleted.Should().BeFalse();

        _time.Advance(PostCommitHook.Timeout);
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        seen.IsCancellationRequested.Should().BeTrue("the abandoned dispatch is told to stop so it releases its scope and connection");
        _logger.Collector.GetSnapshot().Should().ContainSingle(r =>
            r.Level == LogLevel.Warning && r.Message.StartsWith("Broadcast of IncidentUpdated for incident"));
    }

    [Fact]
    public async Task Run_DispatcherThrows_LogsAndDoesNotThrow()
    {
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("hub down")));

        var run = () => Sut.RunAsync(OneEvent());

        await run.Should().NotThrowAsync();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Run_DispatcherCompletes_LogsNothing()
    {
        _dispatcher.DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await Sut.RunAsync(OneEvent());

        _logger.Collector.GetSnapshot().Should().BeEmpty();
    }
}
