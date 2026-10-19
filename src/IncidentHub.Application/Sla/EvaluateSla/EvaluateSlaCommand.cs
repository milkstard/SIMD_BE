using MediatR;

namespace IncidentHub.Application.Sla.EvaluateSla;

/// <summary>One SLA monitor pass over every open incident that is due a warning or a breach. Sent by the Worker.</summary>
public sealed record EvaluateSlaCommand : IRequest<EvaluateSlaResult>;

/// <param name="Failed">Incidents skipped because their save failed (retried on the next pass).</param>
public sealed record EvaluateSlaResult(int Warned, int Breached, int Failed);
