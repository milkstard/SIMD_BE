using FluentAssertions;
using IncidentHub.Infrastructure.Security;

namespace IncidentHub.Api.UnitTests.Security;

public sealed class HtmlSanitizerAdapterTests
{
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    [Fact]
    public void Sanitize_ScriptTag_Removed()
    {
        var result = _sanitizer.Sanitize("<p>Hello</p><script>alert(1)</script>");

        result.Should().Be("<p>Hello</p>");
    }

    [Fact]
    public void Sanitize_OnErrorAttribute_Removed()
    {
        var result = _sanitizer.Sanitize("<img src=\"x.png\" onerror=\"alert(1)\">");

        result.Should().NotContain("onerror").And.Contain("img");
    }

    [Fact]
    public void Sanitize_JavascriptUrl_Removed()
    {
        var result = _sanitizer.Sanitize("<a href=\"javascript:alert(1)\">click</a>");

        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void Sanitize_SafeMarkup_Kept()
    {
        const string html = "<p>Error <strong>500</strong> after <em>payment</em></p><ul><li>one</li></ul>";

        _sanitizer.Sanitize(html).Should().Be(html);
    }

    [Fact]
    public void Sanitize_OnlyScript_ReturnsEmpty()
    {
        _sanitizer.Sanitize("<script>alert(1)</script>").Should().BeEmpty();
    }
}
