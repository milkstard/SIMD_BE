using FluentAssertions;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Notifications.Rendering;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class EmailTemplateRendererTests
{
    private static EmailTemplateRenderer Create(string baseUrl = "https://app.example.com/") =>
        new(Options.Create(new EmailOptions { PublicBaseUrl = baseUrl }));

    [Fact]
    public void Render_HtmlSpecialCharacters_AreEncoded()
    {
        var email = Create().Render(
            "<script>alert(1)</script>",
            [("Title", "<b>\"Checkout\" & more</b>")],
            "INC-1042");

        email.Html.Should().NotContain("<script>").And.NotContain("<b>\"Checkout\"");
        email.Html.Should().Contain("&lt;script&gt;").And.Contain("&amp;");
        email.Text.Should().Contain("<b>\"Checkout\" & more</b>", "the text part is not HTML");
    }

    [Fact]
    public void Render_IncludesIncidentLink()
    {
        var email = Create().Render("Reported", [("Title", "x")], "INC-1042");

        email.Html.Should().Contain("https://app.example.com/incidents/INC-1042");
        email.Text.Should().Contain("https://app.example.com/incidents/INC-1042");
    }

    [Fact]
    public void Render_NumberWithSpecialCharacters_IsEscapedInLink()
    {
        var email = Create().Render("Reported", [], "INC 1/2");

        email.Html.Should().Contain("/incidents/INC%201%2F2");
    }

    [Fact]
    public void Render_NoBaseUrl_OmitsLink()
    {
        var email = Create(string.Empty).Render("Reported", [("Title", "x")], "INC-1042");

        email.Html.Should().NotContain("<a ");
        email.Text.Should().NotContain("http");
    }
}
