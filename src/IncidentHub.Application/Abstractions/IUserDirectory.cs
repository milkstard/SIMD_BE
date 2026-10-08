namespace IncidentHub.Application.Abstractions;

public interface IUserDirectory
{
    /// <summary>Returns the internal user id for an Entra object id, creating the user on first sight.</summary>
    Task<Guid> EnsureUserAsync(string entraObjectId, string displayName, string email, CancellationToken cancellationToken);
}
