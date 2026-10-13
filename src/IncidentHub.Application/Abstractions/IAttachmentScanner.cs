using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Abstractions;

/// <summary>Virus-scan hook. v1 ships a stub; a real scanner (ClamAV, Defender for Storage) replaces it behind this interface.</summary>
public interface IAttachmentScanner
{
    /// <summary>Returns <see cref="ScanStatus.Clean"/> or <see cref="ScanStatus.Infected"/>; throws when the scan could not run.</summary>
    Task<ScanStatus> ScanAsync(Stream content, CancellationToken cancellationToken);
}
