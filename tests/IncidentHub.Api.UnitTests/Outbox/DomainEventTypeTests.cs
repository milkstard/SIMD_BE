using FluentAssertions;
using IncidentHub.Domain.Common;
using IncidentHub.Infrastructure.Outbox;

namespace IncidentHub.Api.UnitTests.Outbox;

public sealed class DomainEventTypeTests
{
    private static readonly Type[] EventTypes = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
        .ToArray();

    [Fact]
    public void DomainEvents_AreDiscovered()
    {
        EventTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void DomainEvents_HaveUniqueNames()
    {
        // OutboxMessage.Type stores the simple class name, and the BE-10 dispatcher maps it back to a type.
        EventTypes.Select(t => t.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DomainEvents_NamesFitOutboxTypeColumn()
    {
        EventTypes.Select(t => t.Name).Should().OnlyContain(n => n.Length <= OutboxMessage.TypeMaxLength);
    }
}
