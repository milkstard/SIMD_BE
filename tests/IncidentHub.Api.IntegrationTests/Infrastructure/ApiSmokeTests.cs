using System.Net;
using FluentAssertions;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ApiSmokeTests(ApiFactory factory)
{
    [Fact]
    public async Task GetSwaggerDocument_InDevelopment_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
