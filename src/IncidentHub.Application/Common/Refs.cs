namespace IncidentHub.Application.Common;

public sealed record TeamRefDto(Guid Id, string Name);

public sealed record UserRefDto(Guid Id, string DisplayName, string Email);
