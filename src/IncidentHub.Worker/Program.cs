using IncidentHub.Application;
using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure;
using IncidentHub.Infrastructure.Telemetry;
using IncidentHub.Worker.Identity;
using IncidentHub.Worker.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddIncidentHubObservability(WorkerTelemetry.Source);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();

var host = builder.Build();
host.Run();
