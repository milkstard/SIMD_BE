using FluentAssertions;
using IncidentHub.Application.Notifications;

namespace IncidentHub.Application.UnitTests.Notifications;

public sealed class NotificationPrefsParserTests
{
    [Theory]
    [InlineData("{\"channel\":\"Email\"}", NotificationChannel.Email)]
    [InlineData("{\"channel\":\"Teams\"}", NotificationChannel.Teams)]
    [InlineData("{\"channel\":\"Slack\"}", NotificationChannel.Slack)]
    [InlineData("{\"channel\":\"teams\"}", NotificationChannel.Teams)]
    [InlineData("{\"Channel\":\"SLACK\"}", NotificationChannel.Slack)]
    [InlineData("{\"other\":1,\"channel\":\"Slack\"}", NotificationChannel.Slack)]
    public void Parse_ValidChannel_CaseInsensitive(string json, NotificationChannel expected) =>
        NotificationPrefsParser.Parse(json).Should().Be(expected);

    [Fact]
    public void Parse_Default_ReturnsEmail() =>
        NotificationPrefsParser.Parse(IncidentHub.Domain.Users.User.DefaultNotificationPrefs).Should().Be(NotificationChannel.Email);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"channel\":")]
    [InlineData("[]")]
    [InlineData("\"Teams\"")]
    public void Parse_MalformedOrEmpty_ReturnsNull(string? json) =>
        NotificationPrefsParser.Parse(json).Should().BeNull();

    [Theory]
    [InlineData("{\"channel\":\"Pager\"}")]
    [InlineData("{\"channel\":\"1\"}")]
    [InlineData("{\"channel\":7}")]
    [InlineData("{\"channel\":null}")]
    public void Parse_UnknownChannel_ReturnsNull(string json) =>
        NotificationPrefsParser.Parse(json).Should().BeNull();

    [Fact]
    public void Parse_MissingProperty_ReturnsNull() =>
        NotificationPrefsParser.Parse("{\"mode\":\"Teams\"}").Should().BeNull();
}
