using System.Text.Json.Serialization;
using IncidentHub.Api.Auth;
using IncidentHub.Api.Errors;
using IncidentHub.Api.Filters;
using IncidentHub.Api.Hubs;
using IncidentHub.Api.Swagger;
using IncidentHub.Api.Telemetry;
using IncidentHub.Application;
using IncidentHub.Infrastructure;
using IncidentHub.Infrastructure.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddIncidentHubObservability(
    ApiTelemetry.Source,
    tracing => tracing.AddAspNetCoreInstrumentation(options => options.EnrichWithHttpRequest = ApiTelemetry.RedactAccessToken),
    metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddIncidentHubProblemDetails();
builder.Services.AddIncidentHubAuthentication(builder.Configuration);
builder.Services.AddIncidentHubAuthorization();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ETagResultFilter>();

    // A missing required string should reach FluentValidation (422 with field errors) instead of the automatic 400.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
.AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddIncidentHubSwagger(builder.Configuration);

builder.Services.AddSignalR().AddStackExchangeRedis(options =>
{
    options.Configuration = ConfigurationOptions.Parse(IncidentHub.Infrastructure.DependencyInjection.RequireConnectionString(builder.Configuration, "Redis"));
    options.Configuration.AbortOnConnectFail = false;
});

var app = builder.Build();

app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseIncidentHubSwagger(app.Configuration);
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<IncidentsHub>("/hubs/incidents", options => options.CloseOnAuthenticationExpiration = true);

app.Run();

// Exposed for WebApplicationFactory<Program> in IncidentHub.Api.IntegrationTests.
public partial class Program;
