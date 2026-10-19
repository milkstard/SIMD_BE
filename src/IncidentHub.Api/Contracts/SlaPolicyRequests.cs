namespace IncidentHub.Api.Contracts;

/// <summary>Full replace (PUT) of one severity's policy; the expected RowVersion travels in <c>If-Match</c>.</summary>
public sealed record UpdateSlaPolicyRequest(int AckMinutes, int ResolveMinutes, int EscalateAtPercent);
