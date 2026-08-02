using GymStation.Domain.Attendance;
using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Attendance;

public class AttendanceService(GymStationDbContext db)
{
    /// <summary>
    /// Records a check-in claim. Self and guardian check-ins enforce the gym's window;
    /// instructor/staff entries backfill at any time. Idempotent per (session, person):
    /// a repeat check-in returns the existing record, and a Removed record stays removed
    /// until an instructor re-adds it.
    /// </summary>
    public async Task<AttendanceRecord> CheckInAsync(
        Guid sessionId, Guid personId, CheckInSource source, Guid actorUserId, CancellationToken ct = default)
    {
        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.Status == SessionStatus.Cancelled)
        {
            throw new InvalidOperationException("This session was cancelled.");
        }

        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        await AuthorizeAsync(source, person, actorUserId, ct);

        if (source is CheckInSource.Self or CheckInSource.Guardian)
        {
            var settings = await db.GymSettings.SingleAsync(ct);
            var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId, ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;

            if (!CheckInWindow.IsOpen(session, localNow, settings.CheckInWindowMinutes))
            {
                throw new InvalidOperationException("Check-in isn't open for this session.");
            }
        }

        var existing = await db.AttendanceRecords
            .SingleOrDefaultAsync(a => a.SessionId == sessionId && a.PersonId == personId, ct);
        if (existing is not null)
        {
            if (existing.Status == AttendanceStatus.Removed && source == CheckInSource.Instructor)
            {
                existing.Status = AttendanceStatus.Pending;
                existing.Source = source;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            PersonId = personId,
            Source = source,
        };
        db.AttendanceRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    private async Task AuthorizeAsync(CheckInSource source, Person person, Guid actorUserId, CancellationToken ct)
    {
        switch (source)
        {
            case CheckInSource.Self when person.UserId != actorUserId:
                throw new InvalidOperationException("Self check-in must be for your own roster record.");

            case CheckInSource.Guardian:
                var linked = await db.GuardianLinks.AnyAsync(
                    l => l.GuardianUserId == actorUserId && l.ChildPersonId == person.Id, ct);
                if (!linked)
                {
                    throw new InvalidOperationException("You are not a linked guardian for this person.");
                }
                break;

            case CheckInSource.Instructor:
                var actorIsStaff = await db.Persons.AnyAsync(p => p.UserId == actorUserId && !p.Archived
                    && (p.Roles.HasFlag(PersonRoles.Instructor) || p.Roles.HasFlag(PersonRoles.Admin) || p.Roles.HasFlag(PersonRoles.Owner)), ct);
                if (!actorIsStaff)
                {
                    throw new InvalidOperationException("Only instructors or staff can add members to the roll.");
                }
                break;

            case CheckInSource.Self:
                // Own-record check already passed in the pattern above.
                break;

            default:
                // Out-of-range enum values (bad model binding, future callers) must never
                // slip past authorization.
                throw new InvalidOperationException($"Unknown check-in source '{source}'.");
        }
    }

    public async Task SetStatusAsync(Guid recordId, Guid sessionId, AttendanceStatus status, CancellationToken ct = default)
    {
        // The session id must match the record: callers act on a specific roll, and a
        // crafted record id from another session must not be reachable through it.
        var record = await db.AttendanceRecords.SingleOrDefaultAsync(a => a.Id == recordId && a.SessionId == sessionId, ct)
            ?? throw new InvalidOperationException("Attendance record not found for this session in the active gym.");

        record.Status = status;
        record.ConfirmedUtc = status == AttendanceStatus.Confirmed ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ConfirmAllPendingAsync(Guid sessionId, CancellationToken ct = default)
    {
        var pending = await db.AttendanceRecords
            .Where(a => a.SessionId == sessionId && a.Status == AttendanceStatus.Pending)
            .ToListAsync(ct);

        foreach (var record in pending)
        {
            record.Status = AttendanceStatus.Confirmed;
            record.ConfirmedUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return pending.Count;
    }

    /// <summary>
    /// Soft approval: confirm pending records for sessions whose gym-local end + 2h has
    /// passed. Runs under one gym's tenant context (worker iterates gyms). Returns count.
    /// </summary>
    public async Task<int> ConfirmDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;

        var pending = await db.AttendanceRecords
            .Include(a => a.Session)
            .Where(a => a.Status == AttendanceStatus.Pending)
            .ToListAsync(ct);

        var confirmed = 0;
        foreach (var record in pending)
        {
            if (CheckInWindow.AutoConfirmAt(record.Session) <= localNow)
            {
                record.Status = AttendanceStatus.Confirmed;
                record.ConfirmedUtc = nowUtc;
                confirmed++;
            }
        }

        if (confirmed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return confirmed;
    }

    /// <summary>Gym-verified stats from Confirmed records only (the owner-stats tier).</summary>
    public async Task<(double PerWeekAverage, int VerifiedHours, List<WeekCount> WeeklyCounts)> StatsAsync(
        Guid personId, DateOnly today, int weeks = 12, CancellationToken ct = default)
    {
        // Sunday-start calendar weeks, so the chart labels are true week dates.
        var starts = StatWeeks.Starts(today, weeks);
        var from = starts[0];
        var confirmed = await db.AttendanceRecords
            .Include(a => a.Session)
            .Where(a => a.PersonId == personId && a.Status == AttendanceStatus.Confirmed
                && a.Session.Date >= from && a.Session.Date <= today)
            .ToListAsync(ct);

        var byWeek = confirmed
            .GroupBy(r => StatWeeks.SundayOf(r.Session.Date))
            .ToDictionary(g => g.Key, g => g.Count());
        var weekly = starts.Select(s => new WeekCount(s, byWeek.GetValueOrDefault(s))).ToList();
        var minutes = confirmed.Sum(r => r.Session.DurationMinutes);

        return (Math.Round(confirmed.Count / (double)weeks, 1), minutes / 60, weekly);
    }
}
