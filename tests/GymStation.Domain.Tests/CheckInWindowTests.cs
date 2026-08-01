using GymStation.Domain.Attendance;
using GymStation.Domain.Scheduling;

namespace GymStation.Domain.Tests;

public class CheckInWindowTests
{
    private static readonly ClassSession Session = new()
    {
        Name = "No-Gi",
        Date = new DateOnly(2026, 8, 4),
        StartTime = new TimeOnly(18, 0),
        DurationMinutes = 90,
    };

    [Theory]
    [InlineData("2026-08-04T16:59:59", false)] // one second before the window opens
    [InlineData("2026-08-04T17:00:00", true)]  // opens exactly 60 min before
    [InlineData("2026-08-04T18:30:00", true)]  // mid-session
    [InlineData("2026-08-04T19:30:00", true)]  // closes exactly at session end
    [InlineData("2026-08-04T19:30:01", false)] // one second after end
    [InlineData("2026-08-03T18:00:00", false)] // wrong day
    public void WindowRunsFromMinusWindowToSessionEnd(string localNow, bool expected)
    {
        Assert.Equal(expected, CheckInWindow.IsOpen(Session, DateTime.Parse(localNow), windowMinutes: 60));
    }

    [Fact]
    public void AutoConfirm_IsSessionEndPlusTwoHours()
    {
        Assert.Equal(new DateTime(2026, 8, 4, 21, 30, 0), CheckInWindow.AutoConfirmAt(Session));
    }
}
