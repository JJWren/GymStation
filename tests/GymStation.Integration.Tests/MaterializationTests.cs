using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Scheduling;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

/// <summary>
/// The mint ledger (#168): a template-week materializes at most once, so moving
/// (and later deleting) an occurrence never resurrects it as a duplicate —
/// Joshua's Monday→Sunday drag repro.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MaterializationTests(PostgresFixture fixture)
{
    private static readonly DateOnly Sunday = new(2026, 8, 2); // week start (Weeks.WeekOf)
    private static readonly DateOnly Monday = Sunday.AddDays(1);

    private async Task<TenantContext> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Mint {suffix}", Slug = $"mint-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return tenant;
    }

    private static async Task<ClassTemplate> AddTemplateAsync(GymStationDbContext context, string name, DayOfWeek day)
    {
        var template = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Day = day,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 60,
        };
        context.ClassTemplates.Add(template);
        await context.SaveChangesAsync();
        return template;
    }

    [Fact]
    public async Task MovedOccurrence_DoesNotRefillItsVacatedSlot()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Fundamentals", DayOfWeek.Monday);

        var session = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);

        // The exact live repro: drag Monday → Sunday inside the same visible week.
        await schedule.UpdateSessionAsync(session.Id, session.Name, Sunday, session.StartTime, session.DurationMinutes, null);

        var after = (await schedule.GetWeekAsync(Sunday)).Where(s => s.TemplateId == template.Id).ToList();
        Assert.Single(after);
        Assert.Equal(Sunday, after[0].Date);
    }

    [Fact]
    public async Task CrossWeekMove_LeavesTheOriginWeekVacant_AndAbsorbsTheTargetMint()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Adv Gi", DayOfWeek.Monday);

        var session = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);

        // Edge-hover paging move: next week's SAME weekday — the occupied slot must
        // absorb next week's mint instead of colliding or duplicating.
        await schedule.UpdateSessionAsync(session.Id, session.Name, Monday.AddDays(7), session.StartTime, session.DurationMinutes, null);

        var origin = (await schedule.GetWeekAsync(Sunday)).Where(s => s.TemplateId == template.Id).ToList();
        var target = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Where(s => s.TemplateId == template.Id).ToList();
        Assert.Empty(origin);
        Assert.Single(target);
        Assert.Equal(session.Id, target[0].Id);

        // And the origin week stays settled on every later look.
        Assert.Empty((await schedule.GetWeekAsync(Sunday)).Where(s => s.TemplateId == template.Id));
    }

    [Fact]
    public async Task MoveIntoAnUnviewedWeek_OnADifferentDay_StillAbsorbsTheMint()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Kids BJJ", DayOfWeek.Monday);

        var session = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);

        // Move to NEXT week's WEDNESDAY — a week never viewed, a day that is not
        // the template's. The occupancy check must be per template-week, not per
        // slot, or first view mints a fresh Monday copy beside it.
        var targetWednesday = Sunday.AddDays(7 + 3);
        await schedule.UpdateSessionAsync(session.Id, session.Name, targetWednesday, session.StartTime, session.DurationMinutes, null);

        var target = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Where(s => s.TemplateId == template.Id).ToList();
        Assert.Single(target);
        Assert.Equal(targetWednesday, target[0].Date);
    }

    [Fact]
    public async Task TemplateDayChange_DoesNotDoubleMintAMintedWeek()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "No-Gi", DayOfWeek.Monday);

        await schedule.GetWeekAsync(Sunday);
        await schedule.UpdateTemplateAsync(template.Id, "No-Gi", DayOfWeek.Wednesday, new TimeOnly(18, 0), 60, null, []);

        // The already-minted week keeps its single Monday occurrence...
        var minted = (await schedule.GetWeekAsync(Sunday)).Where(s => s.TemplateId == template.Id).ToList();
        Assert.Single(minted);
        Assert.Equal(Monday, minted[0].Date);

        // ...and an unminted future week follows the template's new day.
        var future = (await schedule.GetWeekAsync(Sunday.AddDays(14))).Where(s => s.TemplateId == template.Id).ToList();
        Assert.Single(future);
        Assert.Equal(DayOfWeek.Wednesday, future[0].Date.DayOfWeek);
    }

    [Fact]
    public async Task DeleteCleanSession_RemovesIt_AndTheVacatedSlotStaysVacant()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Open Mat", DayOfWeek.Friday);

        var session = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);
        await schedule.DeleteSessionAsync(session.Id);

        Assert.Null(await context.ClassSessions.SingleOrDefaultAsync(s => s.Id == session.Id));

        // Idempotent: a stale modal's second submit is a no-op, not a scary banner.
        await schedule.DeleteSessionAsync(session.Id);

        // The ledger claim (#168) holds: the deleted class does not resurrect.
        Assert.Empty((await schedule.GetWeekAsync(Sunday)).Where(s => s.TemplateId == template.Id));
    }

    [Fact]
    public async Task DeleteRefuses_WhenHistoryExists_BecauseBothForeignKeysCascade()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Fundamentals", DayOfWeek.Monday);
        var session = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);

        var person = new Domain.People.Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Checked",
            LastName = "In",
            Roles = Domain.People.PersonRoles.Member | Domain.People.PersonRoles.Instructor,
            JoinedOn = new DateOnly(2026, 1, 1),
        };
        context.Persons.Add(person);
        context.AttendanceRecords.Add(new Domain.Attendance.AttendanceRecord
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            PersonId = person.Id,
            Source = Domain.Attendance.CheckInSource.Self,
            Status = Domain.Attendance.AttendanceStatus.Confirmed,
        });
        await context.SaveChangesAsync();

        // Check-in history refuses — a cascade would silently destroy it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => schedule.DeleteSessionAsync(session.Id));

        // Substitution history refuses the same way (fresh session, swap record only).
        var second = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Single(s => s.TemplateId == template.Id);
        context.SubstitutionRequests.Add(new SubstitutionRequest
        {
            Id = Guid.NewGuid(),
            SessionId = second.Id,
            RequestedByPersonId = person.Id,
            Status = SubstitutionStatus.Declined,
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => schedule.DeleteSessionAsync(second.Id));

        Assert.NotNull(await context.ClassSessions.SingleOrDefaultAsync(s => s.Id == session.Id));
        Assert.NotNull(await context.ClassSessions.SingleOrDefaultAsync(s => s.Id == second.Id));
    }

    [Fact]
    public async Task SeriesUpdate_AppliesForwardOnly_AndTheTemplateFollows()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "No-Gi", DayOfWeek.Monday);

        // Mint three consecutive weeks, then cancel week 3's occurrence.
        var week1 = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);
        var week2 = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Single(s => s.TemplateId == template.Id);
        var week3 = (await schedule.GetWeekAsync(Sunday.AddDays(14))).Single(s => s.TemplateId == template.Id);
        await schedule.CancelSessionAsync(week3.Id, "Holiday");

        // Series edit FROM WEEK 2: rename, retime, and shift Monday → Wednesday.
        await schedule.UpdateSeriesAsync(week2.Id, "No-Gi Advanced", week2.Date.AddDays(2), new TimeOnly(19, 0), 90, null);

        var one = await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week1.Id);
        var two = await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week2.Id);
        var three = await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week3.Id);

        // Past week untouched; pivot + following shifted, renamed, retimed.
        Assert.Equal("No-Gi", one.Name);
        Assert.Equal(DayOfWeek.Monday, one.Date.DayOfWeek);
        Assert.Equal(("No-Gi Advanced", DayOfWeek.Wednesday, new TimeOnly(19, 0)), (two.Name, two.Date.DayOfWeek, two.StartTime));
        Assert.Equal(("No-Gi Advanced", DayOfWeek.Wednesday), (three.Name, three.Date.DayOfWeek));

        // The cancelled sibling follows the series but STAYS cancelled.
        Assert.Equal(SessionStatus.Cancelled, three.Status);

        // Template re-pointed: an unminted week materializes on the new day/time.
        var week4 = (await schedule.GetWeekAsync(Sunday.AddDays(21))).Single(s => s.TemplateId == template.Id);
        Assert.Equal((DayOfWeek.Wednesday, new TimeOnly(19, 0), "No-Gi Advanced"), (week4.Date.DayOfWeek, week4.StartTime, week4.Name));
    }

    [Fact]
    public async Task SeriesUpdate_FullWeekShift_IsIndexSafe_AndCollisionsRefuseFriendly()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Comp Class", DayOfWeek.Monday);

        var week1 = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);
        var week2 = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Single(s => s.TemplateId == template.Id);

        // +7 shift from week 1: every member of the run lands on the NEXT member's
        // old date — only per-statement index checking lets this pass.
        await schedule.UpdateSeriesAsync(week1.Id, "Comp Class", week1.Date.AddDays(7), week1.StartTime, week1.DurationMinutes, null);
        Assert.Equal(week1.Date.AddDays(7), (await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week1.Id)).Date);
        Assert.Equal(week2.Date.AddDays(7), (await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week2.Id)).Date);

        // A -7 shift from the SECOND member now lands on the first's date — an
        // occurrence OUTSIDE the run — and must refuse with words, changing nothing.
        // (This same call also pins the pivot read as no-tracking: a stale
        // identity-resolved pivot would zero the delta and no-op silently.)
        var second = await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week2.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.UpdateSeriesAsync(second.Id, second.Name, second.Date.AddDays(-7), second.StartTime, second.DurationMinutes, null));
        Assert.Equal(week2.Date.AddDays(7), (await context.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == week2.Id)).Date);
    }

    [Fact]
    public async Task SeriesUpdate_RefusesAOneOff()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var oneOff = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = null,
            Date = Monday,
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            Name = "Seminar",
        };
        context.ClassSessions.Add(oneOff);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.UpdateSeriesAsync(oneOff.Id, "Seminar", Monday, new TimeOnly(11, 0), 60, null));
    }

    [Fact]
    public async Task Duplicate_CreatesAOneOffCopy_TypesAndAll_AlwaysScheduled()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var tag = new ClassType { Id = Guid.NewGuid(), Name = "gi", ColorHex = "#C9503B" };
        context.ClassTypes.Add(tag);
        var template = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Fundamentals",
            Day = DayOfWeek.Monday,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 60,
            ClassTypes = [tag],
        };
        context.ClassTemplates.Add(template);
        await context.SaveChangesAsync();

        var source = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);
        await schedule.CancelSessionAsync(source.Id, "Testing");

        // Duplicate the CANCELLED source to Tuesday noon — the copy is a live one-off.
        var copyId = await schedule.DuplicateSessionAsync(source.Id, Monday.AddDays(1), new TimeOnly(12, 0));

        var copy = await context.ClassSessions.AsNoTracking().Include(s => s.ClassTypes).SingleAsync(s => s.Id == copyId);
        Assert.Null(copy.TemplateId);
        Assert.Equal((Monday.AddDays(1), new TimeOnly(12, 0), 60, "Fundamentals"), (copy.Date, copy.StartTime, copy.DurationMinutes, copy.Name));
        Assert.Equal(SessionStatus.Scheduled, copy.Status);
        Assert.Equal([tag.Id], copy.ClassTypes.Select(t => t.Id).ToList());
    }

    [Fact]
    public async Task Duplicate_CoexistsWithTheTemplatesOwnFutureMint()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Comp Class", DayOfWeek.Monday);

        var source = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);

        // Copy onto the template's OWN day next week, before that week is viewed.
        var nextMonday = Monday.AddDays(7);
        await schedule.DuplicateSessionAsync(source.Id, nextMonday, source.StartTime);

        // First view of next week: the one-off must NOT absorb the template's
        // mint — the weekly class appears beside the extra copy.
        var day = (await schedule.GetWeekAsync(Sunday.AddDays(7))).Where(s => s.Date == nextMonday).ToList();
        Assert.Equal(2, day.Count);
        Assert.Single(day.Where(s => s.TemplateId == template.Id));
        Assert.Single(day.Where(s => s.TemplateId == null));
    }

    [Fact]
    public async Task Delete_NeverTouchesAnotherGymsSession()
    {
        var tenant = await SeedGymAsync();
        var otherTenant = await SeedGymAsync();

        Guid foreignSessionId;
        await using (var foreign = fixture.CreateContext(otherTenant))
        {
            var schedule = new ScheduleService(foreign, new NotificationService(foreign));
            var template = await AddTemplateAsync(foreign, "Foreign Class", DayOfWeek.Monday);
            foreignSessionId = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id).Id;
        }

        // A foreign id is indistinguishable from an already-deleted one: no-op.
        await using var context = fixture.CreateContext(tenant);
        await new ScheduleService(context, new NotificationService(context)).DeleteSessionAsync(foreignSessionId);

        await using var verify = fixture.CreateContext(otherTenant);
        Assert.NotNull(await verify.ClassSessions.SingleOrDefaultAsync(s => s.Id == foreignSessionId));
    }

    [Fact]
    public async Task NewTemplate_StillMintsIntoAnAlreadyViewedWeek()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var first = await AddTemplateAsync(context, "Fundamentals", DayOfWeek.Monday);

        await schedule.GetWeekAsync(Sunday);
        var second = await AddTemplateAsync(context, "Bootcamp", DayOfWeek.Thursday);

        var sessions = await schedule.GetWeekAsync(Sunday);
        Assert.Single(sessions.Where(s => s.TemplateId == first.Id));
        Assert.Single(sessions.Where(s => s.TemplateId == second.Id));
    }

    [Fact]
    public async Task DuplicateTemplate_MintsItsFirstOccurrenceOnce_AndCopiesVerbatim()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var gi = new ClassType { Id = Guid.NewGuid(), Name = "gi" };
        context.ClassTypes.Add(gi);
        var source = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Adv Gi",
            Day = DayOfWeek.Monday,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 90,
            ClassTypes = [gi],
        };
        context.ClassTemplates.Add(source);
        await context.SaveChangesAsync();
        await schedule.GetWeekAsync(Sunday);

        var firstOccurrenceId = await schedule.DuplicateTemplateAsync(source.Id, DayOfWeek.Thursday, new TimeOnly(19, 30), Sunday);

        var occurrence = await context.ClassSessions.Include(s => s.ClassTypes).SingleAsync(s => s.Id == firstOccurrenceId);
        Assert.Equal(Sunday.AddDays(4), occurrence.Date);
        Assert.Equal(new TimeOnly(19, 30), occurrence.StartTime);
        Assert.Equal("Adv Gi", occurrence.Name);
        Assert.Equal(90, occurrence.DurationMinutes);
        Assert.NotEqual(source.Id, occurrence.TemplateId);
        Assert.Single(occurrence.ClassTypes, t => t.Id == gi.Id);

        var copy = await context.ClassTemplates.SingleAsync(t => t.Id == occurrence.TemplateId);
        Assert.True(copy.Active);
        Assert.Equal(Sunday.AddDays(4), copy.StartDate);

        // The claim written at duplicate time absorbs the week — one occurrence
        // each, ever, for both the copy and the untouched source.
        var week = await schedule.GetWeekAsync(Sunday);
        Assert.Single(week, s => s.TemplateId == copy.Id);
        Assert.Single(week, s => s.TemplateId == source.Id);
    }

    [Fact]
    public async Task DuplicateTemplate_FromAPausedSource_LandsActive_AndSoftensAStaleInstructor()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var coach = new Domain.People.Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Coach",
            LastName = "T",
            UserId = Guid.NewGuid(),
            Roles = Domain.People.PersonRoles.Instructor,
            JoinedOn = new DateOnly(2026, 1, 1),
        };
        context.Persons.Add(coach);
        var source = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "No-Gi",
            Day = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(6, 0),
            DurationMinutes = 60,
            DefaultInstructorPersonId = coach.Id,
            Active = false,
        };
        context.ClassTemplates.Add(source);
        await context.SaveChangesAsync();

        coach.Archived = true;
        await context.SaveChangesAsync();

        var firstOccurrenceId = await schedule.DuplicateTemplateAsync(source.Id, DayOfWeek.Saturday, new TimeOnly(9, 0), Sunday);

        var occurrence = await context.ClassSessions.SingleAsync(s => s.Id == firstOccurrenceId);
        var copy = await context.ClassTemplates.SingleAsync(t => t.Id == occurrence.TemplateId);
        Assert.True(copy.Active);
        Assert.Null(copy.DefaultInstructorPersonId);
        Assert.False((await context.ClassTemplates.SingleAsync(t => t.Id == source.Id)).Active);
    }

    [Fact]
    public async Task StartDate_BoundsMinting_AndLegacyNullStaysUnbounded()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var bounded = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Bounded",
            Day = DayOfWeek.Monday,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 60,
            StartDate = Sunday,
        };
        var legacy = new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Legacy",
            Day = DayOfWeek.Monday,
            StartTime = new TimeOnly(19, 0),
            DurationMinutes = 60,
        };
        context.ClassTemplates.AddRange(bounded, legacy);
        await context.SaveChangesAsync();

        var earlier = await schedule.GetWeekAsync(Sunday.AddDays(-14));
        Assert.DoesNotContain(earlier, s => s.TemplateId == bounded.Id);
        Assert.Single(earlier, s => s.TemplateId == legacy.Id);

        // Pre-start weeks stay claimless — skipping must not burn the ledger.
        Assert.False(await context.ClassTemplateWeeks.AnyAsync(w => w.TemplateId == bounded.Id));

        var own = await schedule.GetWeekAsync(Sunday);
        Assert.Single(own, s => s.TemplateId == bounded.Id);
    }

    [Fact]
    public async Task CreateTemplateAsync_StampsAStartDate_AndValidatesShape()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var id = await schedule.CreateTemplateAsync("Open Mat", DayOfWeek.Sunday, new TimeOnly(10, 0), 120, null, []);
        var created = await context.ClassTemplates.SingleAsync(t => t.Id == id);
        Assert.NotNull(created.StartDate);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.CreateTemplateAsync("Too Short", DayOfWeek.Monday, new TimeOnly(10, 0), 10, null, []));
    }

    [Fact]
    public async Task DuplicateTemplate_MissingSource_Throws()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.DuplicateTemplateAsync(Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(9, 0), Sunday));
    }

    [Fact]
    public async Task Promote_MakesTheSourceOccurrenceOne_AndTheSeriesFlowsFromIt()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var tuesday = Sunday.AddDays(2);
        var oneOff = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = null,
            Date = tuesday,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 75,
            Name = "Takedown Seminar",
        };
        context.ClassSessions.Add(oneOff);
        await context.SaveChangesAsync();

        await schedule.PromoteToTemplateAsync(oneOff.Id);

        var promoted = await context.ClassSessions.SingleAsync(s => s.Id == oneOff.Id);
        Assert.NotNull(promoted.TemplateId);

        var template = await context.ClassTemplates.SingleAsync(t => t.Id == promoted.TemplateId);
        Assert.Equal(DayOfWeek.Tuesday, template.Day);
        Assert.Equal(new TimeOnly(18, 0), template.StartTime);
        Assert.Equal(75, template.DurationMinutes);
        Assert.Equal("Takedown Seminar", template.Name);
        Assert.Equal(tuesday, template.StartDate);
        Assert.True(template.Active);

        // Week one is claimed at promote time — its view never mints a twin...
        var own = await schedule.GetWeekAsync(Sunday);
        Assert.Single(own, s => s.TemplateId == template.Id);

        // ...the next week mints occurrence #2 on the same weekday...
        var next = await schedule.GetWeekAsync(Sunday.AddDays(7));
        var second = Assert.Single(next, s => s.TemplateId == template.Id);
        Assert.NotEqual(oneOff.Id, second.Id);
        Assert.Equal(tuesday.AddDays(7), second.Date);

        // ...and earlier weeks never see it (ADR 0004).
        Assert.DoesNotContain(await schedule.GetWeekAsync(Sunday.AddDays(-7)), s => s.TemplateId == template.Id);
    }

    [Fact]
    public async Task Promote_RefusesTemplateBornSessions_AndASecondPromote()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var template = await AddTemplateAsync(context, "Fundamentals", DayOfWeek.Monday);

        var minted = (await schedule.GetWeekAsync(Sunday)).Single(s => s.TemplateId == template.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => schedule.PromoteToTemplateAsync(minted.Id));

        var oneOff = new ClassSession { Id = Guid.NewGuid(), Date = Sunday.AddDays(3), StartTime = new TimeOnly(9, 0), DurationMinutes = 60, Name = "Pop-up" };
        context.ClassSessions.Add(oneOff);
        await context.SaveChangesAsync();

        await schedule.PromoteToTemplateAsync(oneOff.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => schedule.PromoteToTemplateAsync(oneOff.Id));
    }

    [Fact]
    public async Task Promote_KeepsACancelledSourceCancelled()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var oneOff = new ClassSession
        {
            Id = Guid.NewGuid(),
            Date = Sunday.AddDays(5),
            StartTime = new TimeOnly(19, 0),
            DurationMinutes = 60,
            Name = "Open Mat Special",
            Status = SessionStatus.Cancelled,
            CancelledReason = "Flooded mats",
        };
        context.ClassSessions.Add(oneOff);
        await context.SaveChangesAsync();

        await schedule.PromoteToTemplateAsync(oneOff.Id);

        var promoted = await context.ClassSessions.SingleAsync(s => s.Id == oneOff.Id);
        Assert.NotNull(promoted.TemplateId);
        Assert.Equal(SessionStatus.Cancelled, promoted.Status);
        Assert.Equal("Flooded mats", promoted.CancelledReason);
    }

    [Fact]
    public async Task UndefinedWeekday_RefusesEverywhere()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        var source = await AddTemplateAsync(context, "Fundamentals", DayOfWeek.Monday);

        // Enum.TryParse accepts out-of-range numerics — the services must not.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.DuplicateTemplateAsync(source.Id, (DayOfWeek)9, new TimeOnly(9, 0), Sunday));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.CreateTemplateAsync("Ghost Day", (DayOfWeek)9, new TimeOnly(9, 0), 60, null, []));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => schedule.UpdateTemplateAsync(source.Id, "Fundamentals", (DayOfWeek)9, new TimeOnly(9, 0), 60, null, []));
    }
}
