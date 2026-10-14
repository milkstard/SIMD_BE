using FluentAssertions;
using IncidentHub.Application.Notifications;

namespace IncidentHub.Application.UnitTests.Notifications;

public sealed class NotificationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_EmptyRecipient_Throws(string address)
    {
        var act = () => new Notification(Guid.NewGuid(), address, null, "Subject", "text", "<p>html</p>");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_EmptySubject_Throws()
    {
        var act = () => new Notification(Guid.NewGuid(), "a@example.com", null, " ", "text", "<p>html</p>");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_EmptyMessageId_Throws()
    {
        var act = () => new Notification(Guid.Empty, "a@example.com", null, "Subject", "text", "<p>html</p>");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_ValidValues_KeepsThem()
    {
        var id = Guid.NewGuid();

        var notification = new Notification(id, "a@example.com", "Ann", "Subject", "text", "<p>html</p>");

        notification.MessageId.Should().Be(id);
        notification.ToAddress.Should().Be("a@example.com");
        notification.ToName.Should().Be("Ann");
    }
}
