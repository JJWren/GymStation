using GymStation.Domain.Scheduling;

namespace GymStation.Domain.Tests;

public class WeeksTests
{
    // 2026-08-02 is a Sunday; every day of that week snaps back to it.
    [Theory]
    [InlineData("2026-08-02", "2026-08-02")] // Sunday itself
    [InlineData("2026-08-03", "2026-08-02")] // Monday
    [InlineData("2026-08-04", "2026-08-02")]
    [InlineData("2026-08-05", "2026-08-02")]
    [InlineData("2026-08-06", "2026-08-02")]
    [InlineData("2026-08-07", "2026-08-02")]
    [InlineData("2026-08-08", "2026-08-02")] // Saturday — last day of the week
    [InlineData("2026-08-09", "2026-08-09")] // next Sunday starts the next week
    public void WeekOf_SnapsToSunday(string date, string expected)
        => Assert.Equal(DateOnly.Parse(expected), Weeks.WeekOf(DateOnly.Parse(date)));

    [Fact]
    public void StatWeeks_ShareTheProductWeekStart()
        => Assert.Equal(
            Weeks.WeekOf(new DateOnly(2026, 8, 5)),
            Domain.Attendance.StatWeeks.SundayOf(new DateOnly(2026, 8, 5)));
}
