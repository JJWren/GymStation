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

    // A late class running past midnight: starts 23:30, ends 00:30 the next day.
    // EndTime (a TimeOnly) wraps, which used to slam the window shut a day early.
    private static readonly ClassSession MidnightSession = new()
    {
        Name = "Late Open Mat",
        Date = new DateOnly(2026, 8, 4),
        StartTime = new TimeOnly(23, 30),
        DurationMinutes = 60,
    };

    [Theory]
    [InlineData("2026-08-04T22:30:00", true)]  // opens 60 min before start
    [InlineData("2026-08-04T23:45:00", true)]  // mid-session, before midnight
    [InlineData("2026-08-05T00:15:00", true)]  // mid-session, after midnight
    [InlineData("2026-08-05T00:30:00", true)]  // closes exactly at session end
    [InlineData("2026-08-05T00:30:01", false)] // one second after end
    [InlineData("2026-08-04T00:20:00", false)] // same clock time a day early
    public void WindowSurvivesMidnight(string localNow, bool expected)
    {
        Assert.Equal(expected, CheckInWindow.IsOpen(MidnightSession, DateTime.Parse(localNow), windowMinutes: 60));
    }

    [Fact]
    public void AutoConfirm_LandsOnTheNextDayForMidnightSessions()
    {
        Assert.Equal(new DateTime(2026, 8, 5, 2, 30, 0), CheckInWindow.AutoConfirmAt(MidnightSession));
    }
}
