using FluentAssertions;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Notifications.Rendering;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.UnitTests.Notifications;

/// <summary>The email wording (Application) through the real renderer (Infrastructure).</summary>
public sealed class EmailContentRenderingTests
{
    private static readonly EmailTemplateRenderer Renderer =
        new(Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example.com" }));

    private static IncidentEmailFacts Facts(string title) => new(
        "INC-1042", title, "Checkout <app>", AppEnvironment.Production, Severity.High, IncidentStatus.Resolved, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Build_TitleWithMarkup_IsEncoded()
    {
        var content = EmailContentBuilder.Resolved(Facts("<script>alert(1)</script> & more"), "Raised <b>timeout</b>");

        var email = Renderer.Render(content.Headline, content.Facts, content.IncidentNumber);

        email.Html.Should().NotContain("<script>").And.NotContain("<b>timeout</b>").And.NotContain("<app>");
        email.Html.Should().Contain("&lt;script&gt;").And.Contain("&lt;b&gt;timeout").And.Contain("&lt;app&gt;");
        email.Text.Should().Contain("<script>alert(1)</script> & more", "the text part is not HTML");
    }

    [Fact]
    public void Build_Reported_LinksToTheIncident()
    {
        var content = EmailContentBuilder.Reported(Facts("Checkout fails"));

        var email = Renderer.Render(content.Headline, content.Facts, content.IncidentNumber);

        email.Html.Should().Contain("https://app.example.com/incidents/INC-1042");
        email.Text.Should().Contain("https://app.example.com/incidents/INC-1042");
    }
}
