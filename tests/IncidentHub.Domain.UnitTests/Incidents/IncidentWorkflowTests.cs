using FluentAssertions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class IncidentWorkflowTests
{
    // Independent copy of the CLAUDE.md table: (From, To, Responder, Assignee, Reporter).
    private static readonly (IncidentStatus From, IncidentStatus To, bool Responder, bool Assignee, bool Reporter)[] Rows =
    [
        (IncidentStatus.New, IncidentStatus.Triaged, true, false, false),
        (IncidentStatus.New, IncidentStatus.Closed, true, false, false),
        (IncidentStatus.Triaged, IncidentStatus.InProgress, true, false, false),
        (IncidentStatus.Triaged, IncidentStatus.OnHold, true, false, false),
        (IncidentStatus.InProgress, IncidentStatus.Resolved, true, true, false),
        (IncidentStatus.InProgress, IncidentStatus.OnHold, true, true, false),
        (IncidentStatus.OnHold, IncidentStatus.InProgress, true, true, false),
        (IncidentStatus.Resolved, IncidentStatus.Closed, true, false, true),
        (IncidentStatus.Resolved, IncidentStatus.Reopened, true, false, true),
        (IncidentStatus.Reopened, IncidentStatus.InProgress, true, false, false),
    ];

    private static readonly IncidentStatus[] AllStatuses = Enum.GetValues<IncidentStatus>();

    private static readonly ActorKind[] AllKinds = Enum.GetValues<ActorKind>();

    public static TheoryData<IncidentStatus, IncidentStatus, ActorKind> PermittedCases() =>
        Cases(permitted: true);

    public static TheoryData<IncidentStatus, IncidentStatus, ActorKind> NotPermittedCases() =>
        Cases(permitted: false);

    public static TheoryData<IncidentStatus, IncidentStatus> UndefinedCases()
    {
        var data = new TheoryData<IncidentStatus, IncidentStatus>();
        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                if (!Rows.Any(r => r.From == from && r.To == to))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    public static TheoryData<IncidentStatus, ActorKind> StatusAndKind()
    {
        var data = new TheoryData<IncidentStatus, ActorKind>();
        foreach (var status in AllStatuses)
        {
            foreach (var kind in AllKinds)
            {
                data.Add(status, kind);
            }
        }

        return data;
    }

    [Fact]
    public void Table_UndefinedPairs_CountIsFortyNineMinusTen() =>
        UndefinedCases().Count.Should().Be(AllStatuses.Length * AllStatuses.Length - Rows.Length);

    [Theory]
    [MemberData(nameof(PermittedCases))]
    public void TransitionTo_AllowedTransitionForPermittedActor_ChangesStatus(IncidentStatus from, IncidentStatus to, ActorKind kind)
    {
        var fixture = IncidentTestData.Build(from);
        var (note, reason, duplicateOf) = IncidentTestData.InputsFor(from, to);

        fixture.Incident.TransitionTo(to, fixture.ActorOf(kind), note, IncidentTestData.Now, reason, duplicateOf);

        fixture.Incident.Status.Should().Be(to);
    }

    [Theory]
    [MemberData(nameof(UndefinedCases))]
    public void TransitionTo_UndefinedTransition_ThrowsInvalidTransition(IncidentStatus from, IncidentStatus to)
    {
        var fixture = IncidentTestData.Build(from);

        var act = () => fixture.Incident.TransitionTo(to, fixture.ActorOf(ActorKind.Responder), "note", IncidentTestData.Now, CloseReason.Fixed);

        act.Should().Throw<InvalidTransitionException>().Which.Should().Match<InvalidTransitionException>(e => e.From == from && e.To == to);
        fixture.Incident.Status.Should().Be(from);
    }

    [Theory]
    [MemberData(nameof(NotPermittedCases))]
    public void TransitionTo_ActorNotPermitted_ThrowsTransitionNotPermitted(IncidentStatus from, IncidentStatus to, ActorKind kind)
    {
        var fixture = IncidentTestData.Build(from);
        var (note, reason, duplicateOf) = IncidentTestData.InputsFor(from, to);

        var act = () => fixture.Incident.TransitionTo(to, fixture.ActorOf(kind), note, IncidentTestData.Now, reason, duplicateOf);

        act.Should().Throw<TransitionNotPermittedException>();
        fixture.Incident.Status.Should().Be(from);
    }

    [Theory]
    [MemberData(nameof(StatusAndKind))]
    public void AllowedTransitions_ForEachStatusAndActor_MatchesTable(IncidentStatus status, ActorKind kind)
    {
        var fixture = IncidentTestData.Build(status);
        var expected = Rows.Where(r => r.From == status && IsPermitted(r, kind)).Select(r => r.To);

        var allowed = IncidentWorkflow.AllowedTransitions(fixture.Incident, fixture.ActorOf(kind));

        allowed.Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(StatusAndKind))]
    public void IsAllowed_ForEachStatusAndActor_AgreesWithAllowedTransitions(IncidentStatus status, ActorKind kind)
    {
        var fixture = IncidentTestData.Build(status);
        var actor = fixture.ActorOf(kind);

        var allowedByPair = AllStatuses.Where(to => IncidentWorkflow.IsAllowed(status, to, actor, fixture.Incident));

        allowedByPair.Should().BeEquivalentTo(IncidentWorkflow.AllowedTransitions(fixture.Incident, actor));
    }

    [Fact]
    public void AllowedTransitions_ClosedIncident_IsEmptyForEveryone()
    {
        var fixture = IncidentTestData.Build(IncidentStatus.Closed);

        foreach (var kind in AllKinds)
        {
            IncidentWorkflow.AllowedTransitions(fixture.Incident, fixture.ActorOf(kind)).Should().BeEmpty();
        }
    }

    private static TheoryData<IncidentStatus, IncidentStatus, ActorKind> Cases(bool permitted)
    {
        var data = new TheoryData<IncidentStatus, IncidentStatus, ActorKind>();
        foreach (var row in Rows)
        {
            foreach (var kind in AllKinds)
            {
                if (IsPermitted(row, kind) == permitted)
                {
                    data.Add(row.From, row.To, kind);
                }
            }
        }

        return data;
    }

    private static bool IsPermitted(
        (IncidentStatus From, IncidentStatus To, bool Responder, bool Assignee, bool Reporter) row,
        ActorKind kind) => kind switch
        {
            ActorKind.Responder or ActorKind.TeamLead or ActorKind.Admin => row.Responder,
            ActorKind.Assignee => row.Assignee,
            ActorKind.Reporter => row.Reporter,
            _ => false,
        };
}
