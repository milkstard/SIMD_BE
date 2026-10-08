using FluentAssertions;
using FluentValidation;
using IncidentHub.Application.Behaviors;
using MediatR;

namespace IncidentHub.Application.UnitTests.Behaviors;

public sealed class ValidationBehaviorTests
{
    private sealed record Ping(string Text) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.Text).NotEmpty();
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsAndSkipsHandler()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator()]);
        var handlerCalled = false;

        var act = () => behavior.Handle(new Ping(string.Empty), () =>
        {
            handlerCalled = true;
            return Task.FromResult("ok");
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsHandler()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await behavior.Handle(new Ping("hi"), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_NoValidators_CallsHandler()
    {
        var behavior = new ValidationBehavior<Ping, string>([]);

        var result = await behavior.Handle(new Ping(string.Empty), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }
}
