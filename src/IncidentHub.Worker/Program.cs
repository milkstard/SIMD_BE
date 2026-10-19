using IncidentHub.Application;
using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Realtime;
using IncidentHub.Infrastructure.Telemetry;
using IncidentHub.Worker.Identity;
using IncidentHub.Worker.Jobs;
using IncidentHub.Worker.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddIncidentHubObservability(WorkerTelemetry.Source);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorkerChangePublisher();
builder.Services.AddNotificationSenders(builder.Configuration);
builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();
builder.Services.AddHostedService<AttachmentScanJob>();
builder.Services.AddHostedService<OrphanAttachmentJob>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<SlaMonitor>();

var host = builder.Build();
host.Run();
