using GymStation.Domain.Notifications;
using GymStation.Domain.Scheduling;
using GymStation.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Scheduling;

public class ScheduleService(GymStationDbContext db, NotificationService notifications)
{
    /// <summary>
    /// Sessions for [weekStart, weekStart+6], lazily materializing template occurrences.
    /// Idempotent: the (GymId, TemplateId, Date) unique index backstops races.
    /// </summary>
    public async Task<List<ClassSession>> GetWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        var weekEnd = weekStart.AddDays(6);

        var templates = await db.ClassTemplates
            .Where(t => t.Active)
            .Include(t => t.ClassTypes)
            .ToListAsync(ct);

        var existingKeys = (await db.ClassSessions
                .Where(s => s.Date >= weekStart && s.Date <= weekEnd && s.TemplateId != null)
                .Select(s => new { s.TemplateId, s.Date })
                .ToListAsync(ct))
            .Select(x => (x.TemplateId!.Value, x.Date))
            .ToHashSet();

        var created = false;
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            foreach (var template in templates.Where(t => t.Day == date.DayOfWeek))
            {
                if (existingKeys.Contains((template.Id, date)))
                {
                    continue;
                }

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
                // A concurrent request materialized the same slots; the unique index
                // kept the data correct — reload below.
                db.ChangeTracker.Clear();
            }
        }

        return await db.ClassSessions
            .Where(s => s.Date >= weekStart && s.Date <= weekEnd)
            .Include(s => s.ClassTypes)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync(ct);
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

        // Phase 4 broadens this to checked-in members; today the instructor + staff hear about it.
        var recipients = await notifications.StaffUserIdsAsync(ct);
        if (session.InstructorPersonId is { } instructorPersonId)
        {
            recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructorPersonId], ct));
        }

        notifications.Notify(
            recipients,
            NotificationCategory.SessionCancelled,
            $"Cancelled: {session.Name} · {session.Date:ddd dd MMM} {session.StartTime:HH\\:mm}",
            $"{session.Name} on {session.Date:dddd dd MMMM} at {session.StartTime:HH\\:mm} was cancelled. Reason: {reason}",
            "/admin/schedule");

        await db.SaveChangesAsync(ct);
    }
}
