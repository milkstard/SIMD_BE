using FluentAssertions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class AttachmentLinkTests
{
    private static readonly DateTimeOffset Later = IncidentTestData.Now.AddHours(1);

    private static Attachment Upload(Guid uploaderId, string fileName = "shot.png") =>
        Attachment.Create(Guid.NewGuid(), uploaderId, fileName, "image/png", 1024, $"attachments/2026/10/{Guid.NewGuid():N}", IncidentTestData.Now);

    private static List<Attachment> Uploads(Guid uploaderId, int count) =>
        Enumerable.Range(0, count).Select(i => Upload(uploaderId, $"shot-{i}.png")).ToList();

    [Fact]
    public void LinkTo_Unlinked_SetsIncidentAndComment()
    {
        var attachment = Upload(Guid.NewGuid());
        var incidentId = Guid.NewGuid();
        var commentId = Guid.NewGuid();

        attachment.LinkTo(incidentId, commentId);

        attachment.IncidentId.Should().Be(incidentId);
        attachment.CommentId.Should().Be(commentId);
    }

    [Fact]
    public void LinkTo_WithoutComment_LinksToTheIncidentOnly()
    {
        var attachment = Upload(Guid.NewGuid());

        attachment.LinkTo(Guid.NewGuid(), null);

        attachment.CommentId.Should().BeNull();
    }

    [Fact]
    public void LinkTo_AlreadyLinked_Throws()
    {
        var attachment = Upload(Guid.NewGuid());
        var incidentId = Guid.NewGuid();
        attachment.LinkTo(incidentId, null);

        var act = () => attachment.LinkTo(Guid.NewGuid(), null);

        act.Should().Throw<InvalidOperationException>();
        attachment.IncidentId.Should().Be(incidentId);
    }

    [Fact]
    public void LinkTo_Infected_Throws()
    {
        var attachment = Upload(Guid.NewGuid());
        attachment.MarkScanned(ScanStatus.Infected);

        var act = () => attachment.LinkTo(Guid.NewGuid(), null);

        act.Should().Throw<InvalidOperationException>();
        attachment.IncidentId.Should().BeNull();
    }

    [Fact]
    public void LinkTo_Pending_Succeeds()
    {
        var attachment = Upload(Guid.NewGuid());

        var act = () => attachment.LinkTo(Guid.NewGuid(), null);

        act.Should().NotThrow();
    }

    [Fact]
    public void AttachFiles_AppendsOneHistoryRowAndEventPerFile()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(UserRole.Responder);
        var files = Uploads(actor.UserId, 2);
        var historyBefore = fixture.Incident.History.Count;

        fixture.Incident.AttachFiles(files, actor, Later);

        files.Should().OnlyContain(f => f.IncidentId == fixture.Incident.Id && f.CommentId == null);
        fixture.Incident.History.Skip(historyBefore).Should().HaveCount(2).And.OnlyContain(h =>
            h.Field == Incident.AttachmentHistoryField && h.ActorId == actor.UserId && h.CreatedAt == Later);
        fixture.Incident.History[historyBefore].NewValue.Should().Be("shot-0.png");
        fixture.Incident.DomainEvents.OfType<AttachmentAdded>().Should().BeEquivalentTo(
            files.Select(f => new AttachmentAdded(fixture.Incident.Id, f.Id, null, false, actor.UserId, Later)));
    }

    [Fact]
    public void AttachFiles_InternalComment_UsesTheInternalHistoryFieldAndFlagsTheEvent()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(UserRole.Responder);
        var file = Upload(actor.UserId);

        var comment = fixture.Incident.AddComment("<p>Root cause</p>", true, actor, Later, [file]);

        file.CommentId.Should().Be(comment.Id);
        file.IncidentId.Should().Be(fixture.Incident.Id);
        fixture.Incident.History[^1].Field.Should().Be(Incident.InternalAttachmentHistoryField);
        var added = fixture.Incident.DomainEvents.OfType<AttachmentAdded>().Single();
        added.IsInternal.Should().BeTrue();
        added.CommentId.Should().Be(comment.Id);
    }

    [Fact]
    public void AttachFiles_MoreThanTen_Throws()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(UserRole.Responder);
        var files = Uploads(actor.UserId, Attachment.MaxPerAction + 1);

        var act = () => fixture.Incident.AttachFiles(files, actor, Later);

        act.Should().Throw<ArgumentException>();
        files.Should().OnlyContain(f => f.IncidentId == null);
    }

    [Fact]
    public void AttachFiles_ExactlyTen_Succeeds()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(UserRole.Responder);

        var act = () => fixture.Incident.AttachFiles(Uploads(actor.UserId, Attachment.MaxPerAction), actor, Later);

        act.Should().NotThrow();
    }

    [Fact]
    public void AttachFiles_FileOfAnotherUploader_Throws()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(UserRole.Responder);
        var foreign = Upload(Guid.NewGuid());

        var act = () => fixture.Incident.AttachFiles([foreign], actor, Later);

        act.Should().Throw<ArgumentException>();
        foreign.IncidentId.Should().BeNull();
    }

    [Fact]
    public void AttachFiles_SystemActor_Throws()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);

        var act = () => fixture.Incident.AttachFiles([], Actor.System, Later);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Report_WithAttachments_LinksThemInTheSameCall()
    {
        var reporter = IncidentTestData.ActorWithRole(UserRole.Reporter);
        var files = Uploads(reporter.UserId, 2);

        var incident = Incident.Report(
            IncidentTestData.ApplicationId,
            AppEnvironment.Production,
            Severity.High,
            "Checkout fails with 500",
            "<p>Error page.</p>",
            null,
            IncidentSource.Manual,
            reporter,
            IncidentTestData.Now,
            attachments: files);

        files.Should().OnlyContain(f => f.IncidentId == incident.Id);
        incident.History.Select(h => h.Field).Should().Equal(
            nameof(Incident.Status),
            Incident.AttachmentHistoryField,
            Incident.AttachmentHistoryField);
        incident.DomainEvents.OfType<IncidentReported>().Should().ContainSingle();
        incident.DomainEvents.OfType<AttachmentAdded>().Should().HaveCount(2);
    }

    [Fact]
    public void Report_WithoutAttachments_RaisesNoAttachmentEvent()
    {
        var incident = IncidentTestData.Report();

        incident.DomainEvents.OfType<AttachmentAdded>().Should().BeEmpty();
    }
}
