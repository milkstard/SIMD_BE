namespace IncidentHub.Application.Abstractions;

public interface IIncidentTextSearch
{
    /// <summary>Ids of incidents of the given applications whose title or description matches the text, best match first.</summary>
    Task<IReadOnlyList<Guid>> FindIdsAsync(
        string text,
        IReadOnlyCollection<Guid> applicationIds,
        int limit,
        CancellationToken cancellationToken);
}
