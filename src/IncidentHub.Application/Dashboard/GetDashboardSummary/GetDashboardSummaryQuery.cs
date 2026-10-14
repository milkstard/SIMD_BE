using IncidentHub.Application.Dashboard.Dtos;
using MediatR;

namespace IncidentHub.Application.Dashboard.GetDashboardSummary;

/// <param name="Range"><c>24h</c>, <c>7d</c> or <c>30d</c>; null means <c>7d</c>.</param>
public sealed record GetDashboardSummaryQuery(string? Range) : IRequest<DashboardSummaryDto>;
