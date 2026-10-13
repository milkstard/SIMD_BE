using IncidentHub.Api.Auth;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Attachments.GetAttachmentDownload;
using IncidentHub.Application.Attachments.UploadAttachment;
using IncidentHub.Application.Incidents.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/attachments")]
public sealed class AttachmentsController(ISender sender) : ControllerBase
{
    // The file is capped at 25 MiB while it streams; this outer cap only adds room for the multipart framing.
    private const long MaxRequestBytes = (25 + 1) * 1024 * 1024;

    [HttpPost]
    [Authorize(Policy = Policies.CanReportIncident)]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<AttachmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttachmentDto>> Upload(CancellationToken cancellationToken)
    {
        var file = await MultipartFileReader.ReadAsync(Request, cancellationToken);
        var attachment = await sender.Send(
            new UploadAttachmentCommand(file.FileName, file.ContentType, file.Content),
            cancellationToken);
        return Created($"/api/v1/attachments/{attachment.Id}/download", attachment);
    }

    /// <summary>Redirects to a read-only link that expires after 5 minutes. The API never streams the blob.</summary>
    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var uri = await sender.Send(new GetAttachmentDownloadQuery(id), cancellationToken);

        // The link is a credential for five minutes: it must not be cached or reused from a history entry.
        Response.Headers.CacheControl = "no-store";
        return Redirect(uri.ToString());
    }
}
