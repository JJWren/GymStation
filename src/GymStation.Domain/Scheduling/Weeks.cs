namespace GymStation.Domain.Scheduling;

/// <summary>
/// The product-wide calendar week starts on SUNDAY (round-4 decision 8, US-market
/// stance) — schedule grids, member week snaps, template materialization windows,
/// the public strip, and the stat charts all lead with it. This helper is the one
/// seam to widen if a future tenant ever needs a different first day.
/// </summary>
public static class Weeks
{
    /// <summary>The Sunday on or before <paramref name="date"/> (DayOfWeek.Sunday == 0).</summary>
    public static DateOnly WeekOf(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);
}
