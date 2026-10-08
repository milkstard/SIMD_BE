namespace IncidentHub.Application.Common.Paging;

public sealed record Paged<T>(IReadOnlyList<T> Items, string? NextCursor, int? TotalCount);

public static class PageLimits
{
    public const int Default = 25;
    public const int Max = 100;
}
