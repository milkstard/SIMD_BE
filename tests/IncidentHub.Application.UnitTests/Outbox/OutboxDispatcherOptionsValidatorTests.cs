using FluentAssertions;
using IncidentHub.Application.Outbox;

namespace IncidentHub.Application.UnitTests.Outbox;

public sealed class OutboxDispatcherOptionsValidatorTests
{
    private readonly OutboxDispatcherOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        _validator.Validate(null, new OutboxDispatcherOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_CapBelowBase_Fails()
    {
        var options = new OutboxDispatcherOptions { BackoffBase = TimeSpan.FromMinutes(5), BackoffCap = TimeSpan.FromMinutes(1) };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("BackoffCap");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Validate_JitterOutOfRange_Fails(double jitter)
    {
        var result = _validator.Validate(null, new OutboxDispatcherOptions { JitterFraction = jitter });

        result.Failures.Should().ContainSingle().Which.Should().Contain("JitterFraction");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveBatch_Fails(int batch)
    {
        var result = _validator.Validate(null, new OutboxDispatcherOptions { BatchSize = batch });

        result.Failures.Should().ContainSingle().Which.Should().Contain("BatchSize");
    }

    [Fact]
    public void Validate_BatchSizeAtUpperBound_Succeeds()
    {
        var options = new OutboxDispatcherOptions { BatchSize = OutboxDispatcherOptions.MaxBatchSize };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BatchSizeAboveUpperBound_Fails()
    {
        var options = new OutboxDispatcherOptions { BatchSize = OutboxDispatcherOptions.MaxBatchSize + 1 };

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("BatchSize");
    }

    [Fact]
    public void Validate_HandlerTimeoutAboveUpperBound_Fails()
    {
        var options = new OutboxDispatcherOptions { HandlerTimeout = OutboxDispatcherOptions.MaxHandlerTimeout + TimeSpan.FromSeconds(1) };

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("HandlerTimeout");
    }

    [Fact]
    public void Validate_PassBudgetAboveUpperBound_Fails()
    {
        var options = new OutboxDispatcherOptions { PassBudget = OutboxDispatcherOptions.MaxPassBudget + TimeSpan.FromSeconds(1) };

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("PassBudget");
    }

    [Fact]
    public void Validate_BackoffCapAboveUpperBound_Fails()
    {
        var options = new OutboxDispatcherOptions { BackoffCap = OutboxDispatcherOptions.MaxBackoffCap + TimeSpan.FromSeconds(1) };

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("BackoffCap");
    }

    [Fact]
    public void Validate_UpperBoundsThemselves_Succeed()
    {
        var options = new OutboxDispatcherOptions
        {
            HandlerTimeout = OutboxDispatcherOptions.MaxHandlerTimeout,
            PassBudget = OutboxDispatcherOptions.MaxPassBudget,
            BackoffCap = OutboxDispatcherOptions.MaxBackoffCap,
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NonPositiveTimeLimits_ReportsEachField()
    {
        var options = new OutboxDispatcherOptions { HandlerTimeout = TimeSpan.Zero, PassBudget = TimeSpan.FromSeconds(-1) };

        var result = _validator.Validate(null, options);

        result.Failures.Should().HaveCount(2);
        result.Failures.Should().Contain(f => f.Contains("HandlerTimeout")).And.Contain(f => f.Contains("PassBudget"));
    }

    [Fact]
    public void Validate_NonPositiveIntervalsAndAttempts_ReportsEachField()
    {
        var options = new OutboxDispatcherOptions
        {
            PollInterval = TimeSpan.Zero,
            MaxAttempts = 0,
            BackoffBase = TimeSpan.Zero,
        };

        var result = _validator.Validate(null, options);

        result.Failures.Should().HaveCount(3);
    }
}
