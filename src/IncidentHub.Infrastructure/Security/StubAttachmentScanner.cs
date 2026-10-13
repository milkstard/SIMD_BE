using System.Text;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Infrastructure.Security;

/// <summary>
/// v1 stand-in for a real antivirus: every file is <c>Clean</c> unless it starts with the standard EICAR test string.
/// Replace the registration with ClamAV or Defender for Storage; nothing else changes.
/// </summary>
public sealed class StubAttachmentScanner : IAttachmentScanner
{
    private static readonly byte[] Eicar =
        Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

    public async Task<ScanStatus> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[Eicar.Length];
        var total = 0;
        while (total < header.Length)
        {
            var read = await content.ReadAsync(header.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == Eicar.Length && header.AsSpan().SequenceEqual(Eicar) ? ScanStatus.Infected : ScanStatus.Clean;
    }
}
