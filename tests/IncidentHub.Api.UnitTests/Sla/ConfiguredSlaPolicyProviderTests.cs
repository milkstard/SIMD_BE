using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Sla;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.UnitTests.Sla;

public sealed class ConfiguredSlaPolicyProviderTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();
        return new ServiceCollection().AddSlaPolicies(configuration).BuildServiceProvider();
    }

    [Fact]
    public async Task GetAsync_NoConfig_ReturnsBuiltInDefault()
    {
        await using var services = Build();

        var policy = await services.GetRequiredService<ISlaPolicyProvider>().GetAsync(Severity.Critical, CancellationToken.None);

        policy.Should().BeEquivalentTo(new { Severity = Severity.Critical, AckMinutes = 15, ResolveMinutes = 240, EscalateAtPercent = 80 });
    }

    [Fact]
    public async Task GetAsync_ConfiguredSeverity_ReturnsConfiguredValues()
    {
        await using var services = Build(new()
        {
            ["Sla:Defaults:High:AckMinutes"] = "30",
            ["Sla:Defaults:High:ResolveMinutes"] = "120",
            ["Sla:Defaults:High:EscalateAtPercent"] = "70",
        });

        var policy = await services.GetRequiredService<ISlaPolicyProvider>().GetAsync(Severity.High, CancellationToken.None);

        policy.Should().BeEquivalentTo(new { Severity = Severity.High, AckMinutes = 30, ResolveMinutes = 120, EscalateAtPercent = 70 });
    }

    [Fact]
    public async Task GetAsync_PartialConfig_OtherSeveritiesKeepDefaults()
    {
        await using var services = Build(new() { ["Sla:Defaults:High:AckMinutes"] = "30" });
        var provider = services.GetRequiredService<ISlaPolicyProvider>();

        var high = await provider.GetAsync(Severity.High, CancellationToken.None);
        var low = await provider.GetAsync(Severity.Low, CancellationToken.None);

        high.AckMinutes.Should().Be(30);
        high.ResolveMinutes.Should().Be(480);
        low.AckMinutes.Should().Be(480);
    }

    [Fact]
    public async Task StartHost_InvalidConfig_ThrowsOptionsValidationException()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sla:Defaults:Critical:AckMinutes"] = "600",
            ["Sla:Defaults:Critical:ResolveMinutes"] = "60",
        });
        builder.Services.AddSlaPolicies(builder.Configuration);
        using var host = builder.Build();

        var start = () => host.StartAsync();

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Sla:Defaults:Critical:ResolveMinutes");
    }
}
