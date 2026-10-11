using System.Reflection;
using FluentAssertions;
using IncidentHub.Domain.Common;

namespace IncidentHub.Api.UnitTests.Architecture;

/// <summary>
/// Backstop for the build-time guard in Directory.Build.targets: checks the compiled assemblies, so it also catches
/// layer leaks that arrive transitively through a package (docs/specs/solution-layout.md §4, ADR 0001).
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(Entity).Assembly;
    private static readonly Assembly Application = typeof(IncidentHub.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(IncidentHub.Infrastructure.DependencyInjection).Assembly;

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void Domain_References_NoProjectOrFrameworkPackages()
    {
        ReferencedNames(Domain).Should().NotContain(name =>
            name.StartsWith("IncidentHub.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || name.StartsWith("MediatR", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_References_OnlyDomainAndCoreEfCore()
    {
        ReferencedNames(Application).Should().NotContain(name =>
            name == "IncidentHub.Infrastructure"
            || name == "IncidentHub.Api"
            || name == "IncidentHub.Worker"
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || (name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) && name != "Microsoft.EntityFrameworkCore.Abstractions")
            || name.StartsWith("StackExchange.Redis", StringComparison.Ordinal)
            || name.StartsWith("Azure.", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_References_NoHostProjects()
    {
        ReferencedNames(Infrastructure).Should().NotContain(name =>
            name == "IncidentHub.Api" || name == "IncidentHub.Worker");
    }
}
