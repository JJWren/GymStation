using GymStation.Domain.Attendance;

namespace GymStation.Domain.Tests;

public class StatWeeksTests
{
    [Theory]
    [InlineData("2026-08-02", "2026-08-02")] // a Sunday maps to itself
    [InlineData("2026-08-01", "2026-07-26")] // Saturday → prior Sunday (the 7/26 label)
    [InlineData("2026-08-05", "2026-08-02")] // mid-week → its Sunday (the 8/2 label)
    public void SundayOf_SnapsToTheWeekStart(string date, string expected)
        => Assert.Equal(DateOnly.Parse(expected), StatWeeks.SundayOf(DateOnly.Parse(date)));

    [Fact]
    public void Starts_AreConsecutiveSundays_EndingWithTheCurrentWeek()
    {
        var starts = StatWeeks.Starts(new DateOnly(2026, 8, 1), 12);

        Assert.Equal(12, starts.Count);
        Assert.Equal(new DateOnly(2026, 5, 10), starts[0]);
        Assert.Equal(new DateOnly(2026, 7, 26), starts[^1]);
        Assert.All(starts, s => Assert.Equal(DayOfWeek.Sunday, s.DayOfWeek));
        for (var i = 1; i < starts.Count; i++)
        {
            Assert.Equal(7, starts[i].DayNumber - starts[i - 1].DayNumber);
        }
    }
}
