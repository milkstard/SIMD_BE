using FluentAssertions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using static IncidentHub.Domain.UnitTests.Incidents.IncidentTestData;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class IncidentTests
{
    private static readonly DateTimeOffset Later = Now.AddMinutes(30);

    private static Actor Responder() => ActorWithRole(UserRole.Responder);

    // ---- Report ----

    [Fact]
    public void Report_ValidInput_StartsNewWithHistoryAndEvent()
    {
        var reporter = ActorWithRole(UserRole.Reporter);

        var incident = Report(reporter);

        incident.Status.Should().Be(IncidentStatus.New);
        incident.ReporterId.Should().Be(reporter.UserId);
        incident.CreatedAt.Should().Be(Now);
        incident.UpdatedAt.Should().Be(Now);
        incident.Number.Should().Be(0);
        incident.Fingerprint.Should().Be(IncidentFingerprint.Compute(IncidentTestData.ApplicationId, incident.Title));
        incident.AckDueAt.Should().BeNull();
        incident.History.Should().ContainSingle().Which.Should().Match<IncidentHistory>(h =>
            h.Field == "Status" && h.OldValue == null && h.NewValue == "New" && h.ActorId == reporter.UserId && h.CreatedAt == Now);
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentReported>()
            .Which.Should().Match<IncidentReported>(e => e.IncidentId == incident.Id && e.ReporterId == reporter.UserId);
    }

    [Fact]
    public void Report_TitleTooLong_Throws()
    {
        var act = () => Report(title: new string('x', Incident.TitleMaxLength + 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Report_BlankTitle_Throws(string title)
    {
        var act = () => Report(title: title);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Report_EmptyApplicationId_Throws()
    {
        var act = () => Incident.Report(
            Guid.Empty, AppEnvironment.Production, Severity.High, "Title", "Desc", null, IncidentSource.Manual, Responder(), Now);

        act.Should().Throw<ArgumentException>();
    }

    // ---- TransitionTo ----

    [Fact]
    public void TransitionTo_Valid_AppendsStatusHistoryRow()
    {
        var f = Build(IncidentStatus.New);
        var actor = f.ActorOf(ActorKind.Responder);

        f.Incident.TransitionTo(IncidentStatus.Triaged, actor, "looks real", Later);

        var row = f.Incident.History.Last();
        row.Field.Should().Be("Status");
        row.OldValue.Should().Be("New");
        row.NewValue.Should().Be("Triaged");
        row.Note.Should().Be("looks real");
        row.ActorId.Should().Be(actor.UserId);
        row.CreatedAt.Should().Be(Later);
        f.Incident.History.Count(h => h.Field == "Status" && h.NewValue == "Triaged").Should().Be(1);
        f.Incident.UpdatedAt.Should().Be(Later);
        f.Incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentTransitioned>()
            .Which.Should().Match<IncidentTransitioned>(e =>
                e.From == IncidentStatus.New && e.To == IncidentStatus.Triaged && e.ActorId == actor.UserId && e.Note == "looks real");
    }

    [Theory]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Reopened)]
    public void TransitionTo_NoteRequiredButMissing_ThrowsRequirementOnNote(IncidentStatus from, IncidentStatus to)
    {
        var f = Build(from);

        var act = () => f.Incident.TransitionTo(to, f.ActorOf(ActorKind.Responder), "  ", Later);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("note");
        f.Incident.Status.Should().Be(from);
    }

    [Fact]
    public void TransitionTo_NoteTooLong_ThrowsRequirementOnNote()
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(
            IncidentStatus.Triaged, f.ActorOf(ActorKind.Responder), new string('n', Incident.NoteMaxLength + 1), Later);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("note");
    }

    [Fact]
    public void TransitionTo_ClosedWithoutCloseReason_ThrowsRequirement()
    {
        var f = Build(IncidentStatus.Resolved);

        var act = () => f.Incident.TransitionTo(IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("closeReason");
    }

    [Theory]
    [InlineData(CloseReason.Fixed)]
    [InlineData(CloseReason.CannotReproduce)]
    public void TransitionTo_ClosedFromNewWithOtherThanDuplicateOrInvalid_ThrowsRequirement(CloseReason reason)
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, reason);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("closeReason");
    }

    [Fact]
    public void TransitionTo_ClosedFromResolvedAsDuplicate_ThrowsRequirement()
    {
        var f = Build(IncidentStatus.Resolved);

        var act = () => f.Incident.TransitionTo(
            IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, CloseReason.Duplicate, Guid.NewGuid());

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("closeReason");
    }

    [Theory]
    [InlineData(CloseReason.Fixed)]
    [InlineData(CloseReason.Invalid)]
    [InlineData(CloseReason.CannotReproduce)]
    public void TransitionTo_ClosedFromResolvedWithAllowedReason_Closes(CloseReason reason)
    {
        var f = Build(IncidentStatus.Resolved);

        f.Incident.TransitionTo(IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, reason);

        f.Incident.Status.Should().Be(IncidentStatus.Closed);
    }

    [Fact]
    public void TransitionTo_CloseReasonWhenNotClosing_ThrowsRequirement()
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(IncidentStatus.Triaged, f.ActorOf(ActorKind.Responder), null, Later, CloseReason.Fixed);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("closeReason");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitionTo_DuplicateWithoutUsableDuplicateOfId_ThrowsRequirement(bool emptyGuid)
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(
            IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, CloseReason.Duplicate, emptyGuid ? Guid.Empty : null);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("duplicateOfId");
    }

    [Fact]
    public void TransitionTo_DuplicateOfSelf_ThrowsRequirement()
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(
            IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, CloseReason.Duplicate, f.Incident.Id);

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("duplicateOfId");
    }

    [Fact]
    public void TransitionTo_DuplicateOfIdWithoutDuplicateReason_ThrowsRequirement()
    {
        var f = Build(IncidentStatus.New);

        var act = () => f.Incident.TransitionTo(
            IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), null, Later, CloseReason.Invalid, Guid.NewGuid());

        act.Should().Throw<TransitionRequirementException>().Which.Field.Should().Be("duplicateOfId");
    }

    [Fact]
    public void TransitionTo_ClosedAsDuplicate_StoresReasonTargetAndHistory()
    {
        var f = Build(IncidentStatus.New);
        var original = Guid.NewGuid();

        f.Incident.TransitionTo(IncidentStatus.Closed, f.ActorOf(ActorKind.Responder), "dup", Later, CloseReason.Duplicate, original);

        f.Incident.ClosedAt.Should().Be(Later);
        f.Incident.CloseReason.Should().Be(CloseReason.Duplicate);
        f.Incident.DuplicateOfId.Should().Be(original);
        f.Incident.History.Should().Contain(h => h.Field == "CloseReason" && h.NewValue == "Duplicate");
        f.Incident.History.Should().Contain(h => h.Field == "DuplicateOfId" && h.NewValue == original.ToString("D"));
    }

    [Fact]
    public void TransitionTo_FirstLeaveNew_SetsAcknowledgedAtOnceAndNeverResets()
    {
        var f = Build(IncidentStatus.New);
        var responder = f.ActorOf(ActorKind.Responder);

        f.Incident.TransitionTo(IncidentStatus.Triaged, responder, null, Now.AddMinutes(5));
        f.Incident.TransitionTo(IncidentStatus.InProgress, responder, null, Now.AddMinutes(10));

        f.Incident.AcknowledgedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void TransitionTo_Resolved_SetsResolvedAt()
    {
        var f = Build(IncidentStatus.InProgress);

        f.Incident.TransitionTo(IncidentStatus.Resolved, f.ActorOf(ActorKind.Assignee), "patched", Later);

        f.Incident.ResolvedAt.Should().Be(Later);
    }

    [Fact]
    public void TransitionTo_ReopenedThenResolvedAgain_KeepsThenOverwritesResolvedAt()
    {
        var f = Build(IncidentStatus.Resolved);
        var firstResolvedAt = f.Incident.ResolvedAt;
        var responder = f.ActorOf(ActorKind.Responder);

        f.Incident.TransitionTo(IncidentStatus.Reopened, responder, "still broken", Later);
        f.Incident.ResolvedAt.Should().Be(firstResolvedAt);

        f.Incident.TransitionTo(IncidentStatus.InProgress, responder, null, Later.AddMinutes(1));
        f.Incident.TransitionTo(IncidentStatus.Resolved, responder, "fixed", Later.AddMinutes(2));
        f.Incident.ResolvedAt.Should().Be(Later.AddMinutes(2));
    }

    [Fact]
    public void TransitionTo_Closed_SetsClosedAtAndReason()
    {
        var f = Build(IncidentStatus.Resolved);

        f.Incident.TransitionTo(IncidentStatus.Closed, f.ActorOf(ActorKind.Reporter), null, Later, CloseReason.Fixed);

        f.Incident.ClosedAt.Should().Be(Later);
        f.Incident.CloseReason.Should().Be(CloseReason.Fixed);
        f.Incident.DuplicateOfId.Should().BeNull();
    }

    [Fact]
    public void TransitionTo_RejectedTransition_ChangesNothing()
    {
        var f = Build(IncidentStatus.New);
        var historyCount = f.Incident.History.Count;

        var act = () => f.Incident.TransitionTo(IncidentStatus.Resolved, f.ActorOf(ActorKind.Responder), "x", Later);

        act.Should().Throw<InvalidTransitionException>();
        f.Incident.History.Should().HaveCount(historyCount);
        f.Incident.DomainEvents.Should().BeEmpty();
        f.Incident.UpdatedAt.Should().Be(Now);
    }

    // ---- Assign ----

    [Fact]
    public void Assign_NewAssigneeAndTeam_AppendsHistoryPerFieldAndRaisesEvent()
    {
        var incident = Report();
        var assignee = Guid.NewGuid();
        var team = Guid.NewGuid();
        var actor = Responder();

        incident.Assign(assignee, team, actor, Later);

        incident.AssigneeId.Should().Be(assignee);
        incident.AssignedTeamId.Should().Be(team);
        incident.History.Where(h => h.Field is "AssigneeId" or "AssignedTeamId").Should().HaveCount(2);
        incident.History.Should().Contain(h =>
            h.Field == "AssigneeId" && h.OldValue == null && h.NewValue == assignee.ToString("D") && h.ActorId == actor.UserId);
        incident.UpdatedAt.Should().Be(Later);
        incident.DomainEvents.OfType<IncidentAssigned>().Should().ContainSingle()
            .Which.Should().Match<IncidentAssigned>(e => e.AssigneeId == assignee && e.AssignedTeamId == team);
    }

    [Fact]
    public void Assign_OnlyAssigneeChanges_AppendsOneRow()
    {
        var f = Build(IncidentStatus.Triaged);
        var before = f.Incident.History.Count;

        f.Incident.Assign(Guid.NewGuid(), f.Incident.AssignedTeamId, f.ActorOf(ActorKind.Responder), Later);

        f.Incident.History.Should().HaveCount(before + 1);
    }

    [Fact]
    public void Assign_SameAssignee_NoHistoryNoEvent()
    {
        var f = Build(IncidentStatus.Triaged);
        var before = f.Incident.History.Count;

        f.Incident.Assign(f.AssigneeId, null, f.ActorOf(ActorKind.Responder), Later);

        f.Incident.History.Should().HaveCount(before);
        f.Incident.DomainEvents.Should().BeEmpty();
        f.Incident.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Assign_Unassign_ClearsAssignee()
    {
        var f = Build(IncidentStatus.Triaged);

        f.Incident.Assign(null, null, f.ActorOf(ActorKind.Responder), Later);

        f.Incident.AssigneeId.Should().BeNull();
        f.Incident.History.Last().Should().Match<IncidentHistory>(h => h.Field == "AssigneeId" && h.NewValue == null);
    }

    [Fact]
    public void Assign_ClosedIncident_Throws()
    {
        var f = Build(IncidentStatus.Closed);

        var act = () => f.Incident.Assign(Guid.NewGuid(), null, f.ActorOf(ActorKind.Responder), Later);

        act.Should().Throw<IncidentClosedException>();
    }

    [Fact]
    public void Assign_SystemActor_RecordsNullActor()
    {
        var incident = Report();

        incident.Assign(Guid.NewGuid(), null, Actor.System, Later);

        incident.History.Last().ActorId.Should().BeNull();
    }

    // ---- Update ----

    [Fact]
    public void Update_ChangedTitle_RecomputesFingerprintAndAppendsHistory()
    {
        var incident = Report();
        var oldFingerprint = incident.Fingerprint;

        incident.Update(new IncidentChanges { Title = "Payment page returns 500" }, Responder(), Later);

        incident.Title.Should().Be("Payment page returns 500");
        incident.Fingerprint.Should().NotBe(oldFingerprint)
            .And.Be(IncidentFingerprint.Compute(IncidentTestData.ApplicationId, "Payment page returns 500"));
        incident.History.Last().Should().Match<IncidentHistory>(h =>
            h.Field == "Title" && h.OldValue == "Checkout fails with 500" && h.NewValue == "Payment page returns 500");
        incident.UpdatedAt.Should().Be(Later);
        incident.DomainEvents.OfType<IncidentUpdated>().Should().ContainSingle()
            .Which.ChangedFields.Should().Equal("Title");
    }

    [Fact]
    public void Update_MultipleFields_OneRowPerFieldAndOneEvent()
    {
        var incident = Report();
        incident.ClearDomainEvents();
        var before = incident.History.Count;

        incident.Update(
            new IncidentChanges
            {
                Description = "<p>new</p>",
                StepsToReproduce = (string?)null,
                Priority = Priority.P2,
                Environment = AppEnvironment.UAT,
            },
            Responder(),
            Later);

        incident.History.Should().HaveCount(before + 4);
        incident.StepsToReproduce.Should().BeNull();
        incident.Priority.Should().Be(Priority.P2);
        incident.Environment.Should().Be(AppEnvironment.UAT);
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdated>()
            .Which.ChangedFields.Should().BeEquivalentTo("Description", "StepsToReproduce", "Priority", "Environment");
    }

    [Fact]
    public void Update_ChangedApplication_RecomputesFingerprint()
    {
        var incident = Report();
        var newApp = Guid.NewGuid();

        incident.Update(new IncidentChanges { ApplicationId = newApp }, Responder(), Later);

        incident.ApplicationId.Should().Be(newApp);
        incident.Fingerprint.Should().Be(IncidentFingerprint.Compute(newApp, incident.Title));
    }

    [Fact]
    public void Update_NothingChanged_NoHistoryNoEvent()
    {
        var incident = Report();
        incident.ClearDomainEvents();
        var before = incident.History.Count;

        incident.Update(new IncidentChanges { Title = incident.Title, Priority = (Priority?)null }, Responder(), Later);
        incident.Update(new IncidentChanges(), Responder(), Later);

        incident.History.Should().HaveCount(before);
        incident.DomainEvents.Should().BeEmpty();
        incident.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_InvalidValueAmongChanges_AppliesNothing()
    {
        var incident = Report();
        var title = incident.Title;

        var act = () => incident.Update(
            new IncidentChanges { Title = "Fine", Description = new string('d', Incident.DescriptionMaxLength + 1) }, Responder(), Later);

        act.Should().Throw<ArgumentOutOfRangeException>();
        incident.Title.Should().Be(title);
    }

    [Fact]
    public void Update_ClosedIncident_Throws()
    {
        var f = Build(IncidentStatus.Closed);

        var act = () => f.Incident.Update(new IncidentChanges { Title = "New" }, f.ActorOf(ActorKind.Responder), Later);

        act.Should().Throw<IncidentClosedException>();
    }

    // ---- Severity / SLA ----

    [Fact]
    public void ChangeSeverity_WithoutReason_Throws()
    {
        var incident = Report();

        var act = () => incident.ChangeSeverity(Severity.Critical, " ", Later, Later.AddHours(4), Responder(), Later);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeSeverity_Valid_SetsDueTimesAndAppendsHistory()
    {
        var incident = Report();
        incident.ClearDomainEvents();
        var actor = Responder();

        incident.ChangeSeverity(Severity.Critical, "Full outage", Later.AddMinutes(15), Later.AddHours(4), actor, Later);

        incident.Severity.Should().Be(Severity.Critical);
        incident.AckDueAt.Should().Be(Later.AddMinutes(15));
        incident.ResolveDueAt.Should().Be(Later.AddHours(4));
        incident.History.Last().Should().Match<IncidentHistory>(h =>
            h.Field == "Severity" && h.OldValue == "High" && h.NewValue == "Critical" && h.Note == "Full outage" && h.ActorId == actor.UserId);
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdated>()
            .Which.ChangedFields.Should().Equal("Severity");
    }

    [Fact]
    public void ChangeSeverity_SameSeverity_NoChange()
    {
        var incident = Report();
        incident.ClearDomainEvents();
        var before = incident.History.Count;

        incident.ChangeSeverity(Severity.High, "same", Later, Later.AddHours(4), Responder(), Later);

        incident.History.Should().HaveCount(before);
        incident.DomainEvents.Should().BeEmpty();
        incident.AckDueAt.Should().BeNull();
    }

    [Fact]
    public void ChangeSeverity_ClosedIncident_Throws()
    {
        var f = Build(IncidentStatus.Closed);

        var act = () => f.Incident.ChangeSeverity(Severity.Low, "r", Later, Later, f.ActorOf(ActorKind.Responder), Later);

        act.Should().Throw<IncidentClosedException>();
    }

    [Fact]
    public void SetSlaDueTimes_Called_SetsBothWithoutHistoryOrEvent()
    {
        var incident = Report();
        incident.ClearDomainEvents();
        var before = incident.History.Count;

        incident.SetSlaDueTimes(Now.AddHours(1), Now.AddHours(8));

        incident.AckDueAt.Should().Be(Now.AddHours(1));
        incident.ResolveDueAt.Should().Be(Now.AddHours(8));
        incident.History.Should().HaveCount(before);
        incident.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkSlaBreached_FirstCall_FlagsAppendsSystemHistoryAndRaisesEvent()
    {
        var incident = Report();
        incident.ClearDomainEvents();

        incident.MarkSlaBreached(Later);

        incident.IsSlaBreached.Should().BeTrue();
        incident.History.Last().Should().Match<IncidentHistory>(h =>
            h.Field == "IsSlaBreached" && h.OldValue == "False" && h.NewValue == "True" && h.ActorId == null);
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdated>();
    }

    [Fact]
    public void MarkSlaBreached_SecondCall_IsIdempotent()
    {
        var incident = Report();
        incident.MarkSlaBreached(Later);
        incident.ClearDomainEvents();
        var before = incident.History.Count;

        incident.MarkSlaBreached(Later.AddMinutes(1));

        incident.History.Should().HaveCount(before);
        incident.DomainEvents.Should().BeEmpty();
    }

    // ---- Encapsulation ----

    [Theory]
    [InlineData(typeof(Incident))]
    [InlineData(typeof(IncidentHistory))]
    public void Properties_HaveNoPublicSetters(Type type)
    {
        var publiclySettable = type
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        publiclySettable.Should().BeEmpty();
    }

    [Fact]
    public void History_ExposedAsReadOnlyList() =>
        typeof(Incident).GetProperty(nameof(Incident.History))!.PropertyType.Should().Be(typeof(IReadOnlyList<IncidentHistory>));
}
