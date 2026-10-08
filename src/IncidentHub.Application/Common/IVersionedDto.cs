namespace IncidentHub.Application.Common;

/// <summary>A response that carries a concurrency token; the API turns it into an <c>ETag</c> header.</summary>
public interface IVersionedDto
{
    /// <summary>Base64 of the entity's <c>RowVersion</c>.</summary>
    string RowVersion { get; }
}
