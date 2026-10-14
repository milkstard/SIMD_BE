using FluentAssertions;
using IncidentHub.Application.Outbox;

namespace IncidentHub.Application.UnitTests.Outbox;

public sealed class OutboxBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static OutboxDispatcherOptions NoJitter() => new() { JitterFraction = 0 };

    [Fact]
    public void NextAttemptAt_FirstAttempt_UsesBase()
    {
        var next = OutboxBackoff.NextAttemptAt(1, Now, NoJitter(), new Random(1));

        next.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public void NextAttemptAt_Doubles_PerAttempt()
    {
        var options = NoJitter();

        OutboxBackoff.NextAttemptAt(2, Now, options, new Random(1)).Should().Be(Now.AddSeconds(20));
        OutboxBackoff.NextAttemptAt(3, Now, options, new Random(1)).Should().Be(Now.AddSeconds(40));
        OutboxBackoff.NextAttemptAt(4, Now, options, new Random(1)).Should().Be(Now.AddSeconds(80));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    public void NextAttemptAt_BeyondCap_ClampsToCap(int attempts)
    {
        var next = OutboxBackoff.NextAttemptAt(attempts, Now, NoJitter(), new Random(1));

        next.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void NextAttemptAt_Jitter_StaysWithinFraction()
    {
        var options = new OutboxDispatcherOptions { JitterFraction = 0.2 };
        var random = new Random(42);

        for (var i = 0; i < 500; i++)
        {
            var delay = OutboxBackoff.NextAttemptAt(3, Now, options, random) - Now;

            delay.TotalSeconds.Should().BeInRange(40 * 0.8, 40 * 1.2);
        }
    }
}
