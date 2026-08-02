using GymStation.Domain.Scheduling;

namespace GymStation.Domain.Attendance;

/// <summary>
/// Self/guardian check-in opens N minutes before the session (a Gym Setting) and closes
/// at session end. Instructors and staff can backfill at any time — that path skips this.
/// </summary>
public static class CheckInWindow
{
    public static bool IsOpen(ClassSession session, DateTime gymLocalNow, int windowMinutes)
    {
        var start = session.Date.ToDateTime(session.StartTime);
        var opens = start.AddMinutes(-windowMinutes);

        // Add the duration to the start DATETIME — EndTime is a TimeOnly that wraps
        // at midnight, which used to close the window a day early for any session
        // ending past 00:00 gym-local.
        var closes = start.AddMinutes(session.DurationMinutes);
        return gymLocalNow >= opens && gymLocalNow <= closes;
    }

    /// <summary>Session end + 2h: pending records auto-confirm after this instant (gym-local).</summary>
    public static DateTime AutoConfirmAt(ClassSession session)
        => session.Date.ToDateTime(session.StartTime).AddMinutes(session.DurationMinutes).AddHours(2);
}
