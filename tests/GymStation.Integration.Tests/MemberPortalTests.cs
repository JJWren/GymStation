using GymStation.Domain.Attendance;
using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Domain.Training;
using GymStation.Infrastructure.Attendance;
using GymStation.Infrastructure.Tenancy;
using GymStation.Infrastructure.Training;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class MemberPortalTests(PostgresFixture fixture)
{
    private async Task<(TenantContext Tenant, Person Member, Person Staff)> SeedAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Portal {suffix}", Slug = $"portal-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        var member = new Person { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "R", UserId = Guid.NewGuid(), JoinedOn = new DateOnly(2026, 1, 1) };
        var staff = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Owner",
            LastName = "T",
            UserId = Guid.NewGuid(),
            Roles = PersonRoles.Owner | PersonRoles.Admin | PersonRoles.Instructor,
            JoinedOn = new DateOnly(2026, 1, 1),
        };
        context.Persons.AddRange(member, staff);
        await context.SaveChangesAsync();
        return (tenant, member, staff);
    }

    [Fact]
    public async Task Diary_IsScopedToItsAuthor_EvenForStaff()
    {
        var (tenant, member, staff) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var diary = new TrainingDiaryService(context, new GymStation.Infrastructure.People.FamilyService(context));

        await diary.AddAsync(member.UserId!.Value, TrainingEntryKind.RollLog, new DateOnly(2026, 7, 30), null,
            "arm drags", null, [(staff.Id, "Owner T", "2x5 · even")]);

        // The author sees their entry (with the partner tag inside it).
        var mine = await diary.GetMineAsync(member.UserId!.Value);
        Assert.Single(mine);
        Assert.Single(mine[0].Rolls);

        // The gym owner — highest role in the gym, and even the tagged partner — sees nothing:
        // the only read path scopes to the caller's own Person.
        Assert.Empty(await diary.GetMineAsync(staff.UserId!.Value));
    }

    [Fact]
    public async Task EditingAnEntry_ReplacesRollsWholesale_AndStaysAuthorOnly()
    {
        var (tenant, member, staff) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var diary = new TrainingDiaryService(context, new GymStation.Infrastructure.People.FamilyService(context));
        var entry = await diary.AddAsync(member.UserId!.Value, TrainingEntryKind.RollLog, new DateOnly(2026, 7, 30), null,
            "arm drags", null, [(staff.Id, "Owner T", "2x5 · even")]);

        // Nobody but the author can even see the entry, let alone rewrite or delete it.
        Assert.Null(await diary.GetEntryAsync(staff.UserId!.Value, entry.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => diary.UpdateAsync(
            staff.UserId!.Value, entry.Id, TrainingEntryKind.LessonNotes, new DateOnly(2026, 7, 30), null, "hijack", null, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => diary.DeleteAsync(staff.UserId!.Value, entry.Id));

        // Flipping to self-reported still demands positive minutes on the edit path.
        await Assert.ThrowsAsync<InvalidOperationException>(() => diary.UpdateAsync(
            member.UserId!.Value, entry.Id, TrainingEntryKind.SelfReported, new DateOnly(2026, 7, 30), null, "notes", null, []));

        await diary.UpdateAsync(member.UserId!.Value, entry.Id, TrainingEntryKind.RollLog, new DateOnly(2026, 7, 29), null,
            "reworked notes", null, [(null, "visitor", "1x5"), (staff.Id, "Owner T", "3x5")]);

        var updated = await diary.GetEntryAsync(member.UserId!.Value, entry.Id);
        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2026, 7, 29), updated!.EntryDate);
        Assert.Equal("reworked notes", updated.Notes);
        Assert.Equal(2, updated.Rolls.Count);

        // The old roll rows are gone, not orphaned alongside the replacements.
        Assert.Equal(2, await context.TrainingRolls.CountAsync(r => r.TrainingEntryId == entry.Id));
    }

    [Fact]
    public async Task DeletingAnEntry_RemovesItsRollsToo()
    {
        var (tenant, member, staff) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var diary = new TrainingDiaryService(context, new GymStation.Infrastructure.People.FamilyService(context));
        var entry = await diary.AddAsync(member.UserId!.Value, TrainingEntryKind.RollLog, new DateOnly(2026, 7, 30), null,
            null, null, [(staff.Id, "Owner T", "2x5")]);

        await diary.DeleteAsync(member.UserId!.Value, entry.Id);

        Assert.Empty(await context.TrainingEntries.ToListAsync());
        Assert.Empty(await context.TrainingRolls.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => diary.DeleteAsync(member.UserId!.Value, entry.Id));
    }

    [Fact]
    public async Task MonthQuery_ReturnsOnlyThatMonth_NewestFirst()
    {
        var (tenant, member, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var diary = new TrainingDiaryService(context, new GymStation.Infrastructure.People.FamilyService(context));
        foreach (var date in new DateOnly[] { new(2026, 7, 31), new(2026, 8, 1), new(2026, 8, 31), new(2026, 9, 1) })
        {
            await diary.AddAsync(member.UserId!.Value, TrainingEntryKind.LessonNotes, date, null, "n", null, []);
        }

        var august = await diary.GetMonthAsync(member.UserId!.Value, new DateOnly(2026, 8, 1));

        Assert.Equal(2, august.Count);
        Assert.Equal(new DateOnly(2026, 8, 31), august[0].EntryDate);
        Assert.Equal(new DateOnly(2026, 8, 1), august[1].EntryDate);
    }

    [Fact]
    public async Task SelfReportedEntries_NeedPositiveMinutes_AndNeverTouchVerifiedStats()
    {
        var (tenant, member, staff) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var diary = new TrainingDiaryService(context, new GymStation.Infrastructure.People.FamilyService(context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => diary.AddAsync(
            member.UserId!.Value, TrainingEntryKind.SelfReported, new DateOnly(2026, 7, 28), null, "home drilling", null, []));

        await diary.AddAsync(member.UserId!.Value, TrainingEntryKind.SelfReported, new DateOnly(2026, 7, 28), null, "home drilling", 120, []);

        // A confirmed 60-minute session gives the verified tier exactly one hour.
        var session = new ClassSession { Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 27), StartTime = new TimeOnly(18, 0), DurationMinutes = 60, Name = "No-Gi" };
        context.ClassSessions.Add(session);
        await context.SaveChangesAsync();
        var attendance = new AttendanceService(context);
        var record = await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Instructor, staff.UserId!.Value);
        await attendance.SetStatusAsync(record.Id, session.Id, AttendanceStatus.Confirmed);

        var hours = await diary.HoursAsync(member.UserId!.Value);
        Assert.Equal(1, hours.VerifiedHours);
        Assert.Equal(2, hours.SelfReportedHours);
        Assert.Equal(3, hours.TotalHours);

        // Owner-side verified stats ignore the diary completely.
        var stats = await attendance.StatsAsync(member.Id, new DateOnly(2026, 8, 1));
        Assert.Equal(1, stats.VerifiedHours);
    }

    [Fact]
    public async Task Rsvps_AreUniquePerPersonPerEvent()
    {
        var (tenant, member, staff) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var gymEvent = new GymStation.Domain.Events.GymEvent
        {
            Id = Guid.NewGuid(),
            Title = "Coastline Open",
            StartsOn = new DateOnly(2026, 8, 16),
            PublishedByPersonId = staff.Id,
        };
        context.GymEvents.Add(gymEvent);
        context.EventRsvps.Add(new GymStation.Domain.Events.EventRsvp
        {
            Id = Guid.NewGuid(),
            EventId = gymEvent.Id,
            PersonId = member.Id,
            Status = GymStation.Domain.Events.RsvpStatus.Going,
        });
        await context.SaveChangesAsync();

        context.EventRsvps.Add(new GymStation.Domain.Events.EventRsvp
        {
            Id = Guid.NewGuid(),
            EventId = gymEvent.Id,
            PersonId = member.Id,
            Status = GymStation.Domain.Events.RsvpStatus.Interested,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DiaryEntries_AreTenantScoped()
    {
        var (tenantA, memberA, _) = await SeedAsync();
        var (tenantB, _, _) = await SeedAsync();

        await using (var contextA = fixture.CreateContext(tenantA))
        {
            await new TrainingDiaryService(contextA, new GymStation.Infrastructure.People.FamilyService(contextA)).AddAsync(
                memberA.UserId!.Value, TrainingEntryKind.LessonNotes, new DateOnly(2026, 7, 30), null, "notes", null, []);
        }

        await using var contextB = fixture.CreateContext(tenantB);
        Assert.Empty(await contextB.TrainingEntries.ToListAsync());
    }
}
