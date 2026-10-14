using FluentAssertions;
using IncidentHub.Application.Notifications.Email;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class EmailContentBuilderTests
{
    [Fact]
    public void Build_Reported_SubjectAndFacts()
    {
        var content = EmailContentBuilder.Reported(EmailTestKit.Facts());

        content.Subject.Should().Be("[INC-1042] High incident reported: Checkout fails");
        content.IncidentNumber.Should().Be("INC-1042");
        content.Facts.Should().Contain([("Application", "Checkout"), ("Environment", "Production"), ("Severity", "High"), ("Status", "Resolved")]);
        content.Facts.Select(f => f.Label).Should().NotContain("Note");
    }

    [Fact]
    public void Build_Assigned_SubjectNamesTheAssignment()
    {
        EmailContentBuilder.Assigned(EmailTestKit.Facts()).Subject.Should().Be("[INC-1042] Assigned to you: Checkout fails");
    }

    [Fact]
    public void Build_Resolved_IncludesNote()
    {
        var content = EmailContentBuilder.Resolved(EmailTestKit.Facts(), "  Gateway timeout raised to 30 s ");

        content.Subject.Should().Be("[INC-1042] Resolved: Checkout fails");
        content.Facts.Should().Contain(("Note", "Gateway timeout raised to 30 s"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Build_Resolved_WithoutNote_OmitsTheFact(string? note)
    {
        EmailContentBuilder.Resolved(EmailTestKit.Facts(), note).Facts.Select(f => f.Label).Should().NotContain("Note");
    }

    [Fact]
    public void Build_Reported_NeverIncludesTheDescription()
    {
        var content = EmailContentBuilder.Reported(EmailTestKit.Facts());

        content.Facts.Select(f => f.Label).Should().NotContain(["Description", "Steps to reproduce", "Comment"]);
    }
}
