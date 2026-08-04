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
}
