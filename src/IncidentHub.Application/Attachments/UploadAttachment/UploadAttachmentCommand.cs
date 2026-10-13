using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Attachments.UploadAttachment;

/// <summary>The length is not a parameter: a multipart part has none up front, so the handler counts while streaming.</summary>
public sealed record UploadAttachmentCommand(string FileName, string ContentType, Stream Content) : IRequest<AttachmentDto>;
