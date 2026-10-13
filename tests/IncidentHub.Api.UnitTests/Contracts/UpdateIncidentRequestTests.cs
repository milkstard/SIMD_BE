using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using IncidentHub.Api.Contracts;
using IncidentHub.Application.Incidents.UpdateIncident;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Api.UnitTests.Contracts;

public sealed class UpdateIncidentRequestTests
{
    // The options MVC uses: camelCase web defaults plus string enums.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static UpdateIncidentCommand Bind(string json) =>
        JsonSerializer.Deserialize<UpdateIncidentRequest>(json, Options)!.ToCommand(Guid.NewGuid(), [1]);

    [Fact]
    public void Deserialize_AbsentField_NotPresent()
    {
        var command = Bind("""{ "title": "A new title" }""");

        command.Title.HasValue.Should().BeTrue();
        command.Title.Value.Should().Be("A new title");
        command.StepsToReproduce.HasValue.Should().BeFalse();
        command.Priority.HasValue.Should().BeFalse();
        command.Severity.HasValue.Should().BeFalse();
        command.ApplicationId.HasValue.Should().BeFalse();
        command.Environment.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_ExplicitNull_PresentWithNull()
    {
        var command = Bind("""{ "stepsToReproduce": null, "priority": null }""");

        command.StepsToReproduce.HasValue.Should().BeTrue();
        command.StepsToReproduce.Value.Should().BeNull();
        command.Priority.HasValue.Should().BeTrue();
        command.Priority.Value.Should().BeNull();
        command.Title.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_NullTitle_Rejected()
    {
        var command = Bind("""{ "title": null }""");

        command.Title.HasValue.Should().BeTrue();
        new UpdateIncidentValidator().Validate(command).Errors.Should().Contain(e => e.PropertyName == "title");
    }

    [Fact]
    public void Deserialize_EnumsAndReason_Bound()
    {
        var command = Bind("""{ "severity": "Critical", "priority": "P2", "environment": "UAT", "reason": "Wider impact" }""");

        command.Severity.Value.Should().Be(Severity.Critical);
        command.Priority.Value.Should().Be(Priority.P2);
        command.Environment.HasValue.Should().BeTrue();
        command.Reason.Should().Be("Wider impact");
    }
}
