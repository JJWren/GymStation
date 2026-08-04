using GymStation.Domain.Notifications;
using GymStation.Domain.Scheduling;
using GymStation.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Scheduling;

public class ScheduleService(GymStationDbContext db, NotificationService notifications)
{
    /// <summary>
    /// Sessions for the week containing <paramref name="weekStart"/>, lazily
    /// materializing template occurrences. A template-week mints AT MOST ONCE —
    /// the ClassTemplateWeek ledger claim outlives the occurrence, so moving or
    /// deleting it never refills the vacated slot (#168). Idempotent: the ledger's
    /// unique index and the (GymId, TemplateId, Date) session index backstop races.
    /// </summary>
    public async Task<List<ClassSession>> GetWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        weekStart = Weeks.WeekOf(weekStart);
        var weekEnd = weekStart.AddDays(6);

        var templates = await db.ClassTemplates
            .Where(t => t.Active)
            .Include(t => t.ClassTypes)
            .ToListAsync(ct);

        var mintedTemplateIds = (await db.ClassTemplateWeeks
                .Where(w => w.WeekStart == weekStart)
                .Select(w => w.TemplateId)
                .ToListAsync(ct))
            .ToHashSet();

        // ANY occurrence of a template inside the week absorbs the mint — not just
        // one sitting on the template's own day. A row moved IN from another week
        // may land on any weekday (the modal date field and edge-hover paging allow
        // arbitrary moves), and minting beside it would recreate the duplicate.
        var occupiedTemplateIds = (await db.ClassSessions
                .Where(s => s.Date >= weekStart && s.Date <= weekEnd && s.TemplateId != null)
                .Select(s => s.TemplateId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var created = false;
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            foreach (var template in templates.Where(t => t.Day == date.DayOfWeek))
            {
                if (mintedTemplateIds.Contains(template.Id))
                {
                    continue; // claimed — even when the slot sits vacant after a move or delete
                }

                if (!occupiedTemplateIds.Contains(template.Id))
                {
                    db.ClassSessions.Add(new ClassSession
                    {
                        Id = Guid.NewGuid(),
                        TemplateId = template.Id,
                        Date = date,
                        StartTime = template.StartTime,
                        DurationMinutes = template.DurationMinutes,
                        Name = template.Name,
                        InstructorPersonId = template.DefaultInstructorPersonId,
                        ClassTypes = [.. template.ClassTypes],
                    });
                }

                db.ClassTemplateWeeks.Add(new ClassTemplateWeek
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    WeekStart = weekStart,
                });
                created = true;
            }
        }

        if (created)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request minted the same template-weeks; the unique
                // indexes kept the data correct — reload below.
                db.ChangeTracker.Clear();
            }
        }

        return await db.ClassSessions
            .Where(s => s.Date >= weekStart && s.Date <= weekEnd)
            .Include(s => s.ClassTypes)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync(ct);
    }

    private static void ValidateShape(string name, int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new InvalidOperationException("A class name is required (80 characters max).");
        }

        if (durationMinutes is < 15 or > 480)
        {
            throw new InvalidOperationException("Duration must be between 15 and 480 minutes.");
        }
    }

    private async Task ValidateInstructorAsync(Guid? instructorPersonId, CancellationToken ct)
    {
        if (instructorPersonId is { } id && !await db.Persons.AnyAsync(
                p => p.Id == id && !p.Archived && p.Roles.HasFlag(Domain.People.PersonRoles.Instructor), ct))
        {
            throw new InvalidOperationException("Pick an active person with the Instructor role.");
        }
    }

    /// <summary>Edits ONE occurrence in place — including moving it to another DATE
    /// (#131). Time or date changes notify the same audience a cancellation would —
    /// staff, the instructor, and everyone already checked in.</summary>
    public async Task UpdateSessionAsync(
        Guid sessionId, string name, DateOnly date, TimeOnly start, int durationMinutes, Guid? instructorPersonId,
        CancellationToken ct = default)
    {
        ValidateShape(name, durationMinutes);

        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        await ValidateInstructorAsync(instructorPersonId, ct);

        var dateChanged = session.Date != date;

        // The (GymId, TemplateId, Date) unique index would reject this anyway —
        // catch it here with words instead of a 500. The template's own occurrence
        // may already be materialized on the target day.
        if (dateChanged && session.TemplateId is { } templateId
            && await db.ClassSessions.AnyAsync(s => s.TemplateId == templateId && s.Date == date && s.Id != session.Id, ct))
        {
            throw new InvalidOperationException("That class already has its occurrence on that day — edit that one instead.");
        }

        var timeChanged = session.StartTime != start || session.DurationMinutes != durationMinutes;

        session.Name = name.Trim();
        session.Date = date;
        session.StartTime = start;
        session.DurationMinutes = durationMinutes;
        session.InstructorPersonId = instructorPersonId;

        if (timeChanged || dateChanged)
        {
            var recipients = await notifications.StaffUserIdsAsync(ct);
            if (instructorPersonId is { } instructor)
            {
                recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructor], ct));
            }

            var checkedInPersonIds = await db.AttendanceRecords
                .Where(a => a.SessionId == session.Id && a.Status != Domain.Attendance.AttendanceStatus.Removed)
                .Select(a => a.PersonId)
                .ToListAsync(ct);
            recipients.AddRange(await notifications.UserIdsForPersonsAsync(checkedInPersonIds, ct));
            recipients.AddRange(await db.FamilyGuardians
                .Join(db.FamilyMembers.Where(m => m.IsWard && checkedInPersonIds.Contains(m.PersonId)),
                    g => g.FamilyId, m => m.FamilyId, (g, m) => g.GuardianUserId)
                .ToListAsync(ct));

            notifications.Notify(
                recipients,
                NotificationCategory.SessionChanged,
                $"Changed: {session.Name} · now {date:ddd dd MMM} at {start:HH\\:mm}",
                $"{session.Name} now runs {date:dddd dd MMMM} at {start:HH\\:mm} for {durationMinutes} minutes.",
                "/schedule");
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Edits the weekly template. Applies to weeks not yet materialized;
    /// occurrences already on the calendar keep their own values.</summary>
    public async Task UpdateTemplateAsync(
        Guid templateId, string name, DayOfWeek day, TimeOnly start, int durationMinutes,
        Guid? instructorPersonId, IReadOnlyList<Guid> typeIds, CancellationToken ct = default)
    {
        ValidateShape(name, durationMinutes);

        var template = await db.ClassTemplates
            .Include(t => t.ClassTypes)
            .SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("Template not found in the active gym.");

        await ValidateInstructorAsync(instructorPersonId, ct);

        // Every posted type id must resolve — a stale/tampered id must not silently
        // erase tags that were meant to be kept.
        var distinctTypeIds = typeIds.Distinct().ToList();
        var types = await db.ClassTypes.Where(t => distinctTypeIds.Contains(t.Id)).ToListAsync(ct);
        if (types.Count != distinctTypeIds.Count)
        {
            throw new InvalidOperationException("One of the class types no longer exists — reload and try again.");
        }

        template.Name = name.Trim();
        template.Day = day;
        template.StartTime = start;
        template.DurationMinutes = durationMinutes;
        template.DefaultInstructorPersonId = instructorPersonId;

        template.ClassTypes.Clear();
        template.ClassTypes.AddRange(types);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Pause (or restore) a weekly template; paused templates stop materializing.</summary>
    public async Task SetTemplateActiveAsync(Guid templateId, bool active, CancellationToken ct = default)
    {
        var template = await db.ClassTemplates.SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("Template not found in the active gym.");

        template.Active = active;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Hard-deletes one occurrence (#169) — but ONLY a clean one. Attendance and
    /// substitution rows both cascade on session delete, so any recorded history
    /// refuses with words instead of silently destroying it (the rank-remove
    /// doctrine: history stays — cancel covers a class that isn't happening).
    /// The template-week ledger claim (#168) keeps the deleted slot vacant.
    /// </summary>
    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (await db.AttendanceRecords.AnyAsync(a => a.SessionId == session.Id, ct))
        {
            throw new InvalidOperationException("That class has check-in history — history stays. Cancel the session instead.");
        }

        if (await db.SubstitutionRequests.AnyAsync(r => r.SessionId == session.Id, ct))
        {
            throw new InvalidOperationException("That class has substitution history — history stays. Cancel the session instead.");
        }

        var recipients = await notifications.StaffUserIdsAsync(ct);
        if (session.InstructorPersonId is { } instructorPersonId)
        {
            recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructorPersonId], ct));
        }

        notifications.Notify(
            recipients,
            NotificationCategory.SessionCancelled,
            $"Removed: {session.Name} · {session.Date:ddd dd MMM} {session.StartTime:HH\\:mm}",
            $"{session.Name} on {session.Date:dddd dd MMMM} at {session.StartTime:HH\\:mm} was removed from the schedule.",
            "/admin/schedule");

        db.ClassSessions.Remove(session);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.Status == SessionStatus.Cancelled)
        {
            return;
        }

        session.Status = SessionStatus.Cancelled;
        session.CancelledReason = reason;

        var recipients = await notifications.StaffUserIdsAsync(ct);
        if (session.InstructorPersonId is { } instructorPersonId)
        {
            recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructorPersonId], ct));
        }

        // Everyone already checked in (and their guardians) hears about the cancellation.
        var checkedInPersonIds = await db.AttendanceRecords
            .Where(a => a.SessionId == session.Id && a.Status != Domain.Attendance.AttendanceStatus.Removed)
            .Select(a => a.PersonId)
            .ToListAsync(ct);
        recipients.AddRange(await notifications.UserIdsForPersonsAsync(checkedInPersonIds, ct));
        recipients.AddRange(await db.FamilyGuardians
            .Join(db.FamilyMembers.Where(m => m.IsWard && checkedInPersonIds.Contains(m.PersonId)),
                g => g.FamilyId, m => m.FamilyId, (g, m) => g.GuardianUserId)
            .ToListAsync(ct));

        notifications.Notify(
            recipients,
            NotificationCategory.SessionCancelled,
            $"Cancelled: {session.Name} · {session.Date:ddd dd MMM} {session.StartTime:HH\\:mm}",
            $"{session.Name} on {session.Date:dddd dd MMMM} at {session.StartTime:HH\\:mm} was cancelled. Reason: {reason}",
            "/admin/schedule");

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Undo a cancellation — same audience as the cancel notice hears it's back on.</summary>
    public async Task ReopenSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.Status != SessionStatus.Cancelled)
        {
            return;
        }

        session.Status = SessionStatus.Scheduled;
        session.CancelledReason = null;

        var recipients = await notifications.StaffUserIdsAsync(ct);
        if (session.InstructorPersonId is { } instructorPersonId)
        {
            recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructorPersonId], ct));
        }

        var checkedInPersonIds = await db.AttendanceRecords
            .Where(a => a.SessionId == session.Id && a.Status != Domain.Attendance.AttendanceStatus.Removed)
            .Select(a => a.PersonId)
            .ToListAsync(ct);
        recipients.AddRange(await notifications.UserIdsForPersonsAsync(checkedInPersonIds, ct));
        recipients.AddRange(await db.FamilyGuardians
            .Join(db.FamilyMembers.Where(m => m.IsWard && checkedInPersonIds.Contains(m.PersonId)),
                g => g.FamilyId, m => m.FamilyId, (g, m) => g.GuardianUserId)
            .ToListAsync(ct));

        notifications.Notify(
            recipients,
            NotificationCategory.SessionChanged,
            $"Back on: {session.Name} · {session.Date:ddd dd MMM} {session.StartTime:HH\\:mm}",
            $"{session.Name} on {session.Date:dddd dd MMMM} at {session.StartTime:HH\\:mm} is back on the schedule.",
            "/schedule");

        await db.SaveChangesAsync(ct);
    }
}
