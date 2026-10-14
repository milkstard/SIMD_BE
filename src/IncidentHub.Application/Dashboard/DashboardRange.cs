namespace IncidentHub.Application.Dashboard;

/// <summary>The summary window: whole UTC days, ending now. <c>24h</c> is today, <c>7d</c> the last 7 days including today.</summary>
public enum DashboardRange
{
    H24,
    D7,
    D30,
}

public static class DashboardRanges
{
    public static string Token(this DashboardRange range) => range switch
    {
        DashboardRange.H24 => "24h",
        DashboardRange.D7 => "7d",
        _ => "30d",
    };

    public static int Days(this DashboardRange range) => range switch
    {
        DashboardRange.H24 => 1,
        DashboardRange.D7 => 7,
        _ => 30,
    };

    /// <summary>A missing value means <c>7d</c>; anything else that is not a known token fails.</summary>
    public static bool TryParse(string? value, out DashboardRange range)
    {
        switch (value)
        {
            case null:
            case "7d":
                range = DashboardRange.D7;
                return true;
            case "24h":
                range = DashboardRange.H24;
                return true;
            case "30d":
                range = DashboardRange.D30;
                return true;
            default:
                range = DashboardRange.D7; // never reached after validation; the documented default rather than H24
                return false;
        }
    }

    /// <summary>UTC midnight of the first day of the window.</summary>
    public static DateTimeOffset WindowStart(this DashboardRange range, TimeProvider time) =>
        new(time.GetUtcNow().UtcDateTime.Date.AddDays(1 - range.Days()), TimeSpan.Zero);
}
