using FluentAssertions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;

namespace IncidentHub.Api.UnitTests.Outbox;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static OutboxMessage NewMessage() =>
        OutboxMessage.From(new IncidentReported(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AppEnvironment.Production,
            Severity.High,
            IncidentSource.Manual,
            Guid.NewGuid(),
            Now));

    [Fact]
    public void MarkProcessed_SetsProcessedAt()
    {
        var message = NewMessage();

        message.MarkProcessed(Now);

        message.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public void MarkFailed_IncrementsAttemptsAndRecordsErrorAndNextAttempt()
    {
        var message = NewMessage();

        message.MarkFailed("boom", Now.AddMinutes(1));
        message.MarkFailed("boom again", Now.AddMinutes(2));

        message.Attempts.Should().Be(2);
        message.Error.Should().Be("boom again");
        message.NextAttemptAt.Should().Be(Now.AddMinutes(2));
        message.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_LongError_IsTruncated()
    {
        var message = NewMessage();

        message.MarkFailed(new string('x', OutboxMessage.ErrorMaxLength + 500), Now);

        message.Error!.Length.Should().Be(OutboxMessage.ErrorMaxLength);
    }

    [Fact]
    public void Park_RaisesAttemptsToCeilingAndKeepsRowUnprocessed()
    {
        var message = NewMessage();
        message.MarkFailed("first", Now);

        message.Park("giving up", maxAttempts: 8);

        message.Attempts.Should().Be(8);
        message.Error.Should().Be("giving up");
        message.NextAttemptAt.Should().BeNull();
        message.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void Park_AlreadyAtCeiling_StillAdvancesAttempts()
    {
        var message = NewMessage();
        message.Park("one", maxAttempts: 1);

        message.Park("two", maxAttempts: 1);

        message.Attempts.Should().Be(2);
    }
}
