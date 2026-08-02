namespace GymStation.Domain.Attendance;

public record WeekCount(DateOnly WeekStart, int Count);

/// <summary>
/// Sunday-start calendar weeks for the stat charts (labels like 7/26, 8/2 per
/// Joshua's spec). The schedule grids stay Monday-first — different surface.
/// </summary>
public static class StatWeeks
{
    public static DateOnly SundayOf(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);

    /// <summary>The last <paramref name="weeks"/> week-starts, oldest first, ending with the (partial) current week.</summary>
    public static List<DateOnly> Starts(DateOnly today, int weeks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeks);

        var current = SundayOf(today);
        return [.. Enumerable.Range(0, weeks).Select(i => current.AddDays(-7 * (weeks - 1 - i)))];
    }
}
