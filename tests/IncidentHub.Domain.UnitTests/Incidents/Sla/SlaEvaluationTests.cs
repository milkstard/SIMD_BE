using FluentAssertions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using static IncidentHub.Domain.UnitTests.Incidents.IncidentTestData;

namespace IncidentHub.Domain.UnitTests.Incidents.Sla;

public sealed class SlaEvaluationTests
{
    private static readonly DateTimeOffset WarnAt = Now.AddHours(3);
    private static readonly DateTimeOffset DueAt = Now.AddHours(4);

    private static Incident WithDueTimes(IncidentStatus status = IncidentStatus.New)
    {
        var incident = Build(status).Incident;
        incident.SetSlaDueTimes(Now.AddMinutes(15), DueAt, WarnAt);
        return incident;
    }

    [Fact]
    public void Decide_BeforeWarnAt_IsNone() =>
        SlaEvaluation.Decide(WithDueTimes(), WarnAt.AddSeconds(-1)).Should().Be(SlaAction.None);

    [Fact]
    public void Decide_AtWarnAt_Warns() =>
        SlaEvaluation.Decide(WithDueTimes(), WarnAt).Should().Be(SlaAction.Warn);

    [Fact]
    public void Decide_AlreadyWarnedBeforeDue_IsNone()
    {
        var incident = WithDueTimes();
        incident.MarkSlaWarned(WarnAt);

        SlaEvaluation.Decide(incident, WarnAt.AddMinutes(30)).Should().Be(SlaAction.None);
    }

    [Fact]
    public void Decide_AtDueTime_BreachesInsteadOfWarning() =>
        SlaEvaluation.Decide(WithDueTimes(), DueAt).Should().Be(SlaAction.Breach);

    [Fact]
    public void Decide_PastDueAfterWarning_Breaches()
    {
        var incident = WithDueTimes();
        incident.MarkSlaWarned(WarnAt);

        SlaEvaluation.Decide(incident, DueAt.AddMinutes(1)).Should().Be(SlaAction.Breach);
    }

    [Fact]
    public void Decide_AlreadyBreached_IsNone()
    {
        var incident = WithDueTimes();
        incident.MarkSlaBreached(DueAt);

        SlaEvaluation.Decide(incident, DueAt.AddHours(1)).Should().Be(SlaAction.None);
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public void Decide_ResolvedOrClosed_IsNone(IncidentStatus status) =>
        SlaEvaluation.Decide(WithDueTimes(status), DueAt.AddHours(1)).Should().Be(SlaAction.None);

    [Theory]
    [InlineData(IncidentStatus.New)]
    [InlineData(IncidentStatus.Triaged)]
    [InlineData(IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.OnHold)]
    [InlineData(IncidentStatus.Reopened)]
    public void Decide_OpenStatusPastDue_Breaches(IncidentStatus status) =>
        SlaEvaluation.Decide(WithDueTimes(status), DueAt.AddHours(1)).Should().Be(SlaAction.Breach);

    [Fact]
    public void Decide_NoDueTimes_IsNone() =>
        SlaEvaluation.Decide(Build(IncidentStatus.New).Incident, DueAt.AddDays(30)).Should().Be(SlaAction.None);

    [Theory]
    [InlineData(IncidentStatus.New, true)]
    [InlineData(IncidentStatus.Reopened, true)]
    [InlineData(IncidentStatus.OnHold, true)]
    [InlineData(IncidentStatus.Resolved, false)]
    [InlineData(IncidentStatus.Closed, false)]
    public void IsOpen_Status_MatchesOpenDefinition(IncidentStatus status, bool expected) =>
        SlaEvaluation.IsOpen(status).Should().Be(expected);
}
