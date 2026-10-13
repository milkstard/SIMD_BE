using FluentAssertions;
using IncidentHub.Application.Incidents;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;

namespace IncidentHub.Application.UnitTests.Incidents;

public sealed class IncidentPermissionsTests
{
    private static Actor ActorOf(UserRole role, Guid? id = null) => new(id ?? Guid.NewGuid(), new HashSet<UserRole> { role });

    [Theory]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    [InlineData(UserRole.Admin)]
    public void CanAssign_ResponderLevelOnOpenIncident_True(UserRole role)
    {
        IncidentPermissions.CanAssign(IncidentStatus.New, ActorOf(role)).Should().BeTrue();
    }

    [Fact]
    public void CanAssign_ReporterRole_False()
    {
        IncidentPermissions.CanAssign(IncidentStatus.New, ActorOf(UserRole.Reporter)).Should().BeFalse();
    }

    [Fact]
    public void CanAssign_ClosedIncident_False()
    {
        IncidentPermissions.CanAssign(IncidentStatus.Closed, ActorOf(UserRole.Admin)).Should().BeFalse();
    }

    [Fact]
    public void CanEdit_ReporterOnOpenIncident_True()
    {
        var reporter = ActorOf(UserRole.Reporter);

        IncidentPermissions.CanEdit(IncidentStatus.Triaged, reporter.UserId, reporter).Should().BeTrue();
    }

    [Fact]
    public void CanEdit_OtherReporter_False()
    {
        IncidentPermissions.CanEdit(IncidentStatus.Triaged, Guid.NewGuid(), ActorOf(UserRole.Reporter)).Should().BeFalse();
    }

    [Fact]
    public void CanEdit_ResponderOnOpenIncident_True()
    {
        IncidentPermissions.CanEdit(IncidentStatus.Triaged, Guid.NewGuid(), ActorOf(UserRole.Responder)).Should().BeTrue();
    }

    [Fact]
    public void CanEdit_ClosedIncident_False()
    {
        var reporter = ActorOf(UserRole.Admin);

        IncidentPermissions.CanEdit(IncidentStatus.Closed, reporter.UserId, reporter).Should().BeFalse();
    }

    [Fact]
    public void AllowedTransitions_AssigneeOnInProgress_CanResolveOrHold()
    {
        var assignee = ActorOf(UserRole.Reporter);

        var allowed = IncidentPermissions.AllowedTransitions(IncidentStatus.InProgress, Guid.NewGuid(), assignee.UserId, assignee);

        allowed.Should().Equal(IncidentStatus.Resolved, IncidentStatus.OnHold);
    }

    [Fact]
    public void AllowedTransitions_StrangerOnNew_IsEmpty()
    {
        IncidentPermissions.AllowedTransitions(IncidentStatus.New, Guid.NewGuid(), null, ActorOf(UserRole.Reporter)).Should().BeEmpty();
    }
}
