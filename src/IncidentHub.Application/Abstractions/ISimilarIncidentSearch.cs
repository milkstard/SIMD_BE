namespace IncidentHub.Application.Abstractions;

public interface ISimilarIncidentSearch
{
    /// <summary>Ids of open incidents of the application whose title matches the text, best match first.</summary>
    Task<IReadOnlyList<Guid>> FindOpenAsync(Guid applicationId, string title, int limit, CancellationToken cancellationToken);
}
