using IncidentHub.Application;
using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure;
using IncidentHub.Infrastructure.Telemetry;
using IncidentHub.Worker.Identity;
using IncidentHub.Worker.Jobs;
using IncidentHub.Worker.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddIncidentHubObservability(WorkerTelemetry.Source);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();
// Nothing broadcasts from the Worker in 09_02 (needs a cross-process path, BE-12/BE-17).
builder.Services.AddSingleton<IPostCommitDispatcher, NoOpPostCommitDispatcher>();
builder.Services.AddHostedService<AttachmentScanJob>();
builder.Services.AddHostedService<OrphanAttachmentJob>();

var host = builder.Build();
host.Run();
