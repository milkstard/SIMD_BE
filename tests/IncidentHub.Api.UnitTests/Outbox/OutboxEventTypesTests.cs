using System.Runtime.CompilerServices;
using FluentAssertions;
using IncidentHub.Domain.Common;
using IncidentHub.Infrastructure.Outbox;

namespace IncidentHub.Api.UnitTests.Outbox;

public sealed class OutboxEventTypesTests
{
    private readonly OutboxEventTypes _types = new();

    public static IEnumerable<object[]> EventTypes() =>
        typeof(IDomainEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void TryDeserialize_EachDomainEvent_RoundTrips(Type eventType)
    {
        // A default-valued instance is enough: the point is that every event record survives serialize then deserialize.
        var original = (IDomainEvent)RuntimeHelpers.GetUninitializedObject(eventType);
        var message = OutboxMessage.From(original);

        var read = _types.TryDeserialize(message, out var result);

        read.Should().BeTrue();
        result.Should().BeOfType(eventType);
        result.Should().BeEquivalentTo(original, options => options.RespectingRuntimeTypes());
    }

    [Fact]
    public void TryDeserialize_UnknownType_ReturnsFalse()
    {
        var message = WithTypeAndPayload("NoSuchEvent", "{}");

        _types.TryDeserialize(message, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"incidentId\": \"not-a-guid\"}")]
    public void TryDeserialize_BadJson_ReturnsFalse(string payload)
    {
        var message = WithTypeAndPayload("IncidentReported", payload);

        _types.TryDeserialize(message, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    private static OutboxMessage WithTypeAndPayload(string type, string payload)
    {
        var message = (OutboxMessage)RuntimeHelpers.GetUninitializedObject(typeof(OutboxMessage));
        typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.Type))!.SetValue(message, type);
        typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.Payload))!.SetValue(message, payload);
        return message;
    }
}
