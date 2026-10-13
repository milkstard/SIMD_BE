namespace IncidentHub.Application.Abstractions;

public interface IAppTeamReader
{
    Task<bool> IsMemberAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetApplicationIdsForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetTeamIdsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The team that owns the application, or null when the application is unknown.</summary>
    Task<Guid?> GetOwningTeamIdAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>Drops cached membership; call after the team of an application changes.</summary>
    Task InvalidateAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken);
}
