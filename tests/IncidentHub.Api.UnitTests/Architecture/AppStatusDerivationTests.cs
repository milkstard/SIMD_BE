using System.Reflection;
using FluentAssertions;
using IncidentHub.Api.Controllers;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace IncidentHub.Api.UnitTests.Architecture;

/// <summary>The application status is derived from open incidents: no input and no stored field may carry it.</summary>
public sealed class AppStatusDerivationTests
{
    private static readonly Assembly Application = typeof(IncidentHub.Application.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(DashboardController).Assembly;

    private static bool CarriesStatus(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(p => p.PropertyType == typeof(AppStatus) || p.PropertyType == typeof(AppStatus?));

    [Fact]
    public void AppStatus_IsNotInAnyRequestDto()
    {
        var commands = Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(i => i == typeof(IBaseRequest)));
        var apiRequests = Api.GetTypes()
            .Where(t => t.Namespace == "IncidentHub.Api.Contracts" && t.Name.EndsWith("Request", StringComparison.Ordinal));
        var actionParameters = Api.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType);

        commands.Concat(apiRequests).Where(CarriesStatus).Should().BeEmpty();
        actionParameters.Should().NotContain(t => t == typeof(AppStatus) || t == typeof(AppStatus?));
    }

    [Fact]
    public void AppStatus_IsNotAPropertyOfAnyDomainEntity()
    {
        typeof(Entity).Assembly.GetTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t))
            .Where(CarriesStatus)
            .Should().BeEmpty();
    }

    [Fact]
    public void DashboardController_RequiresAuthorization()
    {
        typeof(DashboardController).GetCustomAttributes<AuthorizeAttribute>().Should().NotBeEmpty();
        typeof(DashboardController).GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
    }
}
