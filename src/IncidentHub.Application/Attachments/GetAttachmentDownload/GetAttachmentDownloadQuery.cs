using MediatR;

namespace IncidentHub.Application.Attachments.GetAttachmentDownload;

public sealed record GetAttachmentDownloadQuery(Guid AttachmentId) : IRequest<Uri>;
