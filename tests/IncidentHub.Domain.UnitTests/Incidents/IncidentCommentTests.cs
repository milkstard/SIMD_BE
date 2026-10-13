using FluentAssertions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class IncidentCommentTests
{
    private static readonly DateTimeOffset Later = IncidentTestData.Now.AddHours(1);

    [Theory]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    [InlineData(UserRole.Admin)]
    public void AddComment_Responder_AppendsHistoryAndRaisesEvent(UserRole role)
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var actor = IncidentTestData.ActorWithRole(role);
        var historyBefore = fixture.Incident.History.Count;

        var comment = fixture.Incident.AddComment("<p>Looking into it</p>", false, actor, Later);

        comment.IncidentId.Should().Be(fixture.Incident.Id);
        comment.AuthorId.Should().Be(actor.UserId);
        comment.IsInternal.Should().BeFalse();
        comment.CreatedAt.Should().Be(Later);
        fixture.Incident.History.Should().HaveCount(historyBefore + 1);
        var row = fixture.Incident.History[^1];
        row.Field.Should().Be(Incident.CommentHistoryField);
        row.NewValue.Should().Be(comment.Id.ToString("D"));
        row.ActorId.Should().Be(actor.UserId);
        fixture.Incident.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new CommentAdded(fixture.Incident.Id, comment.Id, false, actor.UserId, Later));
    }

    [Fact]
    public void AddComment_InternalByResponder_UsesInternalHistoryField()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);

        var comment = fixture.Incident.AddComment("<p>Root cause</p>", true, fixture.ActorOf(ActorKind.Responder), Later);

        comment.IsInternal.Should().BeTrue();
        fixture.Incident.History[^1].Field.Should().Be(Incident.InternalCommentHistoryField);
        fixture.Incident.DomainEvents.OfType<CommentAdded>().Single().IsInternal.Should().BeTrue();
    }

    [Theory]
    [InlineData(ActorKind.Reporter)]
    [InlineData(ActorKind.Stranger)]
    [InlineData(ActorKind.Assignee)]
    public void AddComment_InternalByNonResponder_Throws(ActorKind kind)
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var historyBefore = fixture.Incident.History.Count;

        var act = () => fixture.Incident.AddComment("<p>x</p>", true, fixture.ActorOf(kind), Later);

        act.Should().Throw<CommentNotPermittedException>();
        fixture.Incident.History.Should().HaveCount(historyBefore);
        fixture.Incident.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AddComment_PublicByReporter_Succeeds()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.New);

        var comment = fixture.Incident.AddComment("<p>Still happening</p>", false, fixture.ActorOf(ActorKind.Reporter), Later);

        comment.AuthorId.Should().Be(fixture.ReporterId);
    }

    [Fact]
    public void AddComment_SystemActor_Throws()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.New);

        var act = () => fixture.Incident.AddComment("<p>x</p>", false, Actor.System, Later);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddComment_ClosedIncident_Succeeds()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.Closed);

        var comment = fixture.Incident.AddComment("<p>Post-mortem</p>", false, fixture.ActorOf(ActorKind.Responder), Later);

        comment.Should().NotBeNull();
    }

    [Fact]
    public void AddComment_Always_LeavesUpdatedAtAlone()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.InProgress);
        var updatedAt = fixture.Incident.UpdatedAt;

        fixture.Incident.AddComment("<p>x</p>", false, fixture.ActorOf(ActorKind.Responder), Later);

        fixture.Incident.UpdatedAt.Should().Be(updatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddComment_BlankBody_Throws(string body)
    {
        var fixture = IncidentTestData.Build(IncidentStatus.New);

        var act = () => fixture.Incident.AddComment(body, false, fixture.ActorOf(ActorKind.Responder), Later);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddComment_BodyTooLong_Throws()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.New);

        var act = () => fixture.Incident.AddComment(new string('a', Comment.BodyMaxLength + 1), false, fixture.ActorOf(ActorKind.Responder), Later);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
