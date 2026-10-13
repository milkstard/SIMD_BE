using FluentAssertions;
using IncidentHub.Api.Hubs;

namespace IncidentHub.Api.UnitTests.Hubs;

public sealed class IncidentClientContractTests
{
    [Fact]
    public void IIncidentClient_HasOnlyContractMethods()
    {
        var methods = typeof(IIncidentClient).GetMethods().Select(m => m.Name);

        methods.Should().BeEquivalentTo(
            "IncidentCreated", "IncidentUpdated", "CommentAdded", "AppStatusChanged", "DashboardChanged", "NotificationReceived");
    }

    [Fact]
    public void IIncidentClient_MethodsReturnTaskAndTakeOneArgument()
    {
        foreach (var method in typeof(IIncidentClient).GetMethods())
        {
            method.ReturnType.Should().Be(typeof(Task), method.Name);
            method.GetParameters().Should().HaveCount(1, method.Name);
        }
    }

    [Fact]
    public void HubGroups_UseContractPrefixes()
    {
        var id = Guid.NewGuid();

        HubGroups.Dashboard(id).Should().Be($"dashboard:{id:N}");
        HubGroups.Team(id).Should().Be($"team:{id:N}");
        HubGroups.Responders(id).Should().Be($"responders:{id:N}");
        HubGroups.Incident(id).Should().Be($"incident:{id:N}");
        HubGroups.User(id).Should().Be($"user:{id:N}");
    }
}
