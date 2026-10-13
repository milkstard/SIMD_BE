using IncidentHub.Application.Common;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Dashboard.Dtos;

/// <summary>API-CONTRACT §5 <c>DashboardSummary</c>. Contract shape only; produced from BE-11 onwards.</summary>
public sealed record DashboardSummaryDto(
    string Range,
    DateTimeOffset GeneratedAt,
    int OpenTotal,
    int Unassigned,
    int SlaBreached,
    IReadOnlyDictionary<Severity, int> OpenBySeverity,
    IReadOnlyList<OpenByApplicationDto> OpenByApplication,
    double? MttaMinutes,
    double? MttrMinutes,
    IReadOnlyList<TrendPointDto> Trend);

public sealed record OpenByApplicationDto(ApplicationRefDto Application, int Open, int Critical);

/// <param name="Date">Formatted <c>YYYY-MM-DD</c>.</param>
public sealed record TrendPointDto(string Date, int Reported, int Resolved);
