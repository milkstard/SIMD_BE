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
    public void Build_Resolved_NoteWithLineBreaks_StaysOnOneLine()
    {
        var content = EmailContentBuilder.Resolved(EmailTestKit.Facts(), "fixed\r\nStatus: Closed\nSeverity: Low");

        content.Facts.Should().Contain(("Note", "fixed Status: Closed Severity: Low"));
    }

    [Fact]
    public void Build_SlaWarning_SubjectPercentAndDueTime()
    {
        var content = EmailContentBuilder.SlaWarning(EmailTestKit.Facts());

        content.Subject.Should().Be("[INC-1042] SLA warning: Checkout fails");
        content.Facts.Should().Contain([
            ("Warning", "80% of the resolve window used"),
            ("Resolve due", "2026-10-14T13:00:00Z"),
            ("Application", "Checkout"),
            ("Severity", "High"),
        ]);
    }

    [Fact]
    public void Build_SlaWarning_WithoutPolicyPercent_OmitsTheWarningFact()
    {
        var content = EmailContentBuilder.SlaWarning(EmailTestKit.Facts(escalateAtPercent: null));

        content.Facts.Select(f => f.Label).Should().NotContain("Warning").And.Contain("Resolve due");
    }

    [Fact]
    public void Build_SlaBreached_SubjectAndDueTime()
    {
        var content = EmailContentBuilder.SlaBreached(EmailTestKit.Facts());

        content.Subject.Should().Be("[INC-1042] SLA BREACHED: Checkout fails");
        content.Facts.Should().Contain(("Resolve due", "2026-10-14T13:00:00Z"));
        content.Facts.Select(f => f.Label).Should().NotContain(["Description", "Steps to reproduce", "Comment"]);
    }

    [Fact]
    public void Build_AnyMail_CollapsesLineBreaksInTheTitle()
    {
        var facts = EmailTestKit.Facts(title: "Checkout\r\nBcc: evil@example.com\n\nfails");
        var mails = new[]
        {
            EmailContentBuilder.Reported(facts),
            EmailContentBuilder.Assigned(facts),
            EmailContentBuilder.Resolved(facts, null),
            EmailContentBuilder.SlaWarning(facts),
            EmailContentBuilder.SlaBreached(facts),
        };

        foreach (var mail in mails)
        {
            mail.Subject.Should().NotContainAny("\r", "\n").And.Contain("Checkout Bcc: evil@example.com fails");
            mail.Facts.Should().OnlyContain(f => f.Label != "Incident" || (!f.Value.Contains('\r') && !f.Value.Contains('\n')));
        }
    }

    [Fact]
    public void Build_Reported_NeverIncludesTheDescription()
    {
        var content = EmailContentBuilder.Reported(EmailTestKit.Facts());

        content.Facts.Select(f => f.Label).Should().NotContain(["Description", "Steps to reproduce", "Comment"]);
    }
}
