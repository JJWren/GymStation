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
                if (template.StartDate is { } startDate && date < startDate)
                {
                    continue; // before the template exists (ADR 0004) — no session, no claim
                }

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

    private static void ValidateDay(DayOfWeek day)
    {
        // Enum params can carry undefined numerics through model binding; an
        // undefined Day never matches date.DayOfWeek, minting nothing, forever.
        if (!Enum.IsDefined(day))
        {
            throw new InvalidOperationException("Pick a real weekday.");
        }
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

    /// <summary>
    /// "This and following" (#170): updates this occurrence AND every later one of
    /// the same template, then re-points the template so unmaterialized weeks
    /// follow too. A date change shifts the whole run by the same day delta via
    /// the park-and-land two-phase (the MoveRankAsync recipe) — the unique
    /// (GymId, TemplateId, Date) index checks per ROW, so a uniform shift can
    /// transiently collide with its own members (±7 always would). Collisions
    /// with occurrences OUTSIDE the run (earlier history sitting on a landing
    /// date) surface as a friendly refusal and roll everything back. Past
    /// occurrences never change; cancelled future ones follow but stay cancelled.
    /// </summary>
    public async Task UpdateSeriesAsync(
        Guid sessionId, string name, DateOnly date, TimeOnly start, int durationMinutes, Guid? instructorPersonId,
        CancellationToken ct = default)
    {
        ValidateShape(name, durationMinutes);

        // AsNoTracking: the pivot MUST be the database's current date. A tracked
        // read would hand back a stale identity-resolved instance after any
        // earlier shift on this context, silently zeroing the delta.
        var session = await db.ClassSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.TemplateId is not { } templateId)
        {
            throw new InvalidOperationException("That class is a one-off — there is no series behind it.");
        }

        var template = await db.ClassTemplates.SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("The weekly template behind this class no longer exists.");

        await ValidateInstructorAsync(instructorPersonId, ct);

        var pivot = session.Date;
        var dayDelta = date.DayNumber - pivot.DayNumber;
        var trimmed = name.Trim();
        var timeChanged = session.StartTime != start || session.DurationMinutes != durationMinutes;

        // Audience gathered BEFORE the shift: staff, the incoming instructor, and
        // everyone checked in on any affected occurrence (plus their guardians) —
        // the same people a cancellation would reach.
        var affectedIds = await db.ClassSessions
            .Where(s => s.TemplateId == templateId && s.Date >= pivot)
            .Select(s => s.Id)
            .ToListAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (dayDelta == 0)
            {
                await db.ClassSessions
                    .Where(s => s.TemplateId == templateId && s.Date >= pivot)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.Name, trimmed)
                        .SetProperty(s => s.StartTime, start)
                        .SetProperty(s => s.DurationMinutes, durationMinutes)
                        .SetProperty(s => s.InstructorPersonId, instructorPersonId), ct);
            }
            else
            {
                // Park the run ~55 years out (no real occurrence lives there),
                // then land it with the delta applied — each phase is per-row
                // collision-free among the run's own members.
                const int Park = 20000;
                var parked = pivot.AddDays(Park);
                await db.ClassSessions
                    .Where(s => s.TemplateId == templateId && s.Date >= pivot)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.Date, s => s.Date.AddDays(Park)), ct);
                await db.ClassSessions
                    .Where(s => s.TemplateId == templateId && s.Date >= parked)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.Name, trimmed)
                        .SetProperty(s => s.StartTime, start)
                        .SetProperty(s => s.DurationMinutes, durationMinutes)
                        .SetProperty(s => s.InstructorPersonId, instructorPersonId)
                        .SetProperty(s => s.Date, s => s.Date.AddDays(dayDelta - Park)), ct);
            }
        }
        catch (Exception ex) when (
            ex is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
                or DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } })
        {
            // ONLY the landing collision (23505) gets the friendly banner — any
            // other database failure surfaces as itself (#88 precedent).
            throw new InvalidOperationException("That series move lands on this class's own earlier occurrences — move or delete the conflicting classes first.");
        }

        template.Name = trimmed;
        template.Day = date.DayOfWeek;
        template.StartTime = start;
        template.DurationMinutes = durationMinutes;
        template.DefaultInstructorPersonId = instructorPersonId;

        if (timeChanged || dayDelta != 0)
        {
            var recipients = await notifications.StaffUserIdsAsync(ct);
            if (instructorPersonId is { } instructor)
            {
                recipients.AddRange(await notifications.UserIdsForPersonsAsync([instructor], ct));
            }

            var checkedInPersonIds = await db.AttendanceRecords
                .Where(a => affectedIds.Contains(a.SessionId) && a.Status != Domain.Attendance.AttendanceStatus.Removed)
                .Select(a => a.PersonId)
                .Distinct()
                .ToListAsync(ct);
            recipients.AddRange(await notifications.UserIdsForPersonsAsync(checkedInPersonIds, ct));
            recipients.AddRange(await db.FamilyGuardians
                .Join(db.FamilyMembers.Where(m => m.IsWard && checkedInPersonIds.Contains(m.PersonId)),
                    g => g.FamilyId, m => m.FamilyId, (g, m) => g.GuardianUserId)
                .ToListAsync(ct));

            notifications.Notify(
                recipients,
                NotificationCategory.SessionChanged,
                $"Changed: {trimmed} · this and all following classes",
                $"{trimmed} now runs {date.DayOfWeek}s at {start:HH\\:mm} for {durationMinutes} minutes, starting {date:dddd dd MMMM}.",
                "/schedule");
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Edits the weekly template. Applies to weeks not yet materialized;
    /// occurrences already on the calendar keep their own values.</summary>
    public async Task UpdateTemplateAsync(
        Guid templateId, string name, DayOfWeek day, TimeOnly start, int durationMinutes,
        Guid? instructorPersonId, IReadOnlyList<Guid> typeIds, CancellationToken ct = default)
    {
        ValidateDay(day);
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
    /// Creates a weekly template (#179). StartDate = the gym's local today (ADR
    /// 0004), so the new pattern begins with the next matching weekday and never
    /// retro-fills past weeks an admin later browses. Returns the template id.
    /// </summary>
    public async Task<Guid> CreateTemplateAsync(
        string name, DayOfWeek day, TimeOnly start, int durationMinutes,
        Guid? instructorPersonId, IReadOnlyList<Guid> typeIds, CancellationToken ct = default)
    {
        ValidateDay(day);
        ValidateShape(name, durationMinutes);
        await ValidateInstructorAsync(instructorPersonId, ct);

        var distinctTypeIds = typeIds.Distinct().ToList();
        var types = await db.ClassTypes.Where(t => distinctTypeIds.Contains(t.Id)).ToListAsync(ct);
        if (types.Count != distinctTypeIds.Count)
        {
            throw new InvalidOperationException("One of the class types no longer exists — reload and try again.");
        }

        var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);

        var template = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Day = day,
            StartTime = start,
            DurationMinutes = durationMinutes,
            DefaultInstructorPersonId = instructorPersonId,
            StartDate = localToday,
            ClassTypes = types,
        };

        db.ClassTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template.Id;
    }

    /// <summary>
    /// Copies a weekly template to another day/time (#179). The copy is its own
    /// template — verbatim name/duration/instructor/types, always Active (even
    /// from a paused source), StartDate = its first occurrence's date (ADR 0004).
    /// That first occurrence is minted here for the viewed week, claim included,
    /// exactly as GetWeekAsync would — so the editor can open on it immediately
    /// (the #171 visible-confirmation doctrine). Returns the new SESSION id.
    /// </summary>
    public async Task<Guid> DuplicateTemplateAsync(
        Guid templateId, DayOfWeek day, TimeOnly start, DateOnly weekStart, CancellationToken ct = default)
    {
        ValidateDay(day);
        weekStart = Weeks.WeekOf(weekStart);
        var targetDate = weekStart.AddDays((int)day);

        var source = await db.ClassTemplates
            .Include(t => t.ClassTypes)
            .SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("Template not found in the active gym.");

        // Carry the default instructor only while it still points at an active
        // Instructor-role person — a stale assignment softens to unassigned
        // rather than blocking the duplicate.
        var instructorPersonId = source.DefaultInstructorPersonId;
        if (instructorPersonId is { } id && !await db.Persons.AnyAsync(
                p => p.Id == id && !p.Archived && p.Roles.HasFlag(Domain.People.PersonRoles.Instructor), ct))
        {
            instructorPersonId = null;
        }

        var copy = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            Day = day,
            StartTime = start,
            DurationMinutes = source.DurationMinutes,
            DefaultInstructorPersonId = instructorPersonId,
            Active = true,
            StartDate = targetDate,
            ClassTypes = [.. source.ClassTypes],
        };

        var firstOccurrence = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = copy.Id,
            Date = targetDate,
            StartTime = start,
            DurationMinutes = copy.DurationMinutes,
            Name = copy.Name,
            InstructorPersonId = instructorPersonId,
            ClassTypes = [.. source.ClassTypes],
        };

        db.ClassTemplates.Add(copy);
        db.ClassSessions.Add(firstOccurrence);
        db.ClassTemplateWeeks.Add(new ClassTemplateWeek
        {
            Id = Guid.NewGuid(),
            TemplateId = copy.Id,
            WeekStart = weekStart,
        });

        await db.SaveChangesAsync(ct);
        return firstOccurrence.Id;
    }

    /// <summary>
    /// Promotes a one-off class to a weekly template (#180). The source session
    /// BECOMES occurrence #1 — it gets the new TemplateId, its week's ledger
    /// claim is written, and the template inherits everything from it (weekday
    /// from its date, StartDate = that date per ADR 0004), so series edits
    /// propagate from the class the admin is looking at. Status is untouched:
    /// a cancelled one-off promotes into a cancelled first occurrence.
    /// </summary>
    public async Task PromoteToTemplateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ClassSessions
            .Include(s => s.ClassTypes)
            .SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.TemplateId is not null)
        {
            throw new InvalidOperationException("That class already follows a weekly template.");
        }

        // Same softening as duplication: a stale instructor becomes unassigned.
        var instructorPersonId = session.InstructorPersonId;
        if (instructorPersonId is { } id && !await db.Persons.AnyAsync(
                p => p.Id == id && !p.Archived && p.Roles.HasFlag(Domain.People.PersonRoles.Instructor), ct))
        {
            instructorPersonId = null;
        }

        var template = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = session.Name,
            Day = session.Date.DayOfWeek,
            StartTime = session.StartTime,
            DurationMinutes = session.DurationMinutes,
            DefaultInstructorPersonId = instructorPersonId,
            Active = true,
            StartDate = session.Date,
            ClassTypes = [.. session.ClassTypes],
        };

        session.TemplateId = template.Id;

        db.ClassTemplates.Add(template);
        db.ClassTemplateWeeks.Add(new ClassTemplateWeek
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            WeekStart = Weeks.WeekOf(session.Date),
        });

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
        // Exclusive row lock BEFORE the history checks: check-ins and sub requests
        // take KEY SHARE on this row through their FKs, so a concurrent insert
        // either commits first (the checks below see it and refuse) or queues
        // behind the lock and fails on the vanished parent — never cascaded away
        // silently between a check and the delete.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Tenant predicate lives IN the raw SQL: the composed global filter still
        // wraps this query, but a foreign gym's row must never be locked even
        // transiently, and the guarantee should be visible right here.
        var session = (await db.ClassSessions
                .FromSqlInterpolated($"""SELECT * FROM "ClassSessions" WHERE "Id" = {sessionId} AND "GymId" = {db.CurrentGymId} FOR UPDATE""")
                .ToListAsync(ct))
            .SingleOrDefault();
        if (session is null)
        {
            return; // idempotent: already gone (stale modal, double submit) — the goal state holds
        }

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
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Copies one class to another day/time (#171). The copy is a ONE-OFF
    /// (TemplateId = null) by design: an extra class, deliberately untethered
    /// from the weekly series, so it can never collide with materialization or
    /// series edits — the template's own occurrence still mints beside it.
    /// Always lands Scheduled, whatever the source's status. Returns the new id.
    /// </summary>
    public async Task<Guid> DuplicateSessionAsync(Guid sessionId, DateOnly date, TimeOnly start, CancellationToken ct = default)
    {
        var source = await db.ClassSessions
            .Include(s => s.ClassTypes)
            .SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        var copy = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = null,
            Date = date,
            StartTime = start,
            DurationMinutes = source.DurationMinutes,
            Name = source.Name,
            InstructorPersonId = source.InstructorPersonId,
            ClassTypes = [.. source.ClassTypes],
        };

        db.ClassSessions.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy.Id;
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
