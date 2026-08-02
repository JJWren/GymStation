using GymStation.Domain.Attendance;
using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Attendance;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Scheduling;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class AttendanceTests(PostgresFixture fixture)
{
    private async Task<(TenantContext Tenant, Person Member, Person Kid, Guid GuardianUserId, ClassSession Session)> SeedAsync(
        DateOnly? sessionDate = null, TimeOnly? start = null)
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Att {suffix}", Slug = $"att-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        context.GymSettings.Add(new GymSettings { GymId = gym.Id });

        var member = new Person { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "R", UserId = Guid.NewGuid(), JoinedOn = new DateOnly(2026, 1, 1) };
        var kid = new Person { Id = Guid.NewGuid(), FirstName = "Leo", LastName = "P", JoinedOn = new DateOnly(2026, 1, 1) };
        var guardianUserId = Guid.NewGuid();
        context.Persons.AddRange(member, kid);
        context.GuardianLinks.Add(new GuardianLink { Id = Guid.NewGuid(), GuardianUserId = guardianUserId, ChildPersonId = kid.Id });

        var utcNow = DateTime.UtcNow;
        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            Date = sessionDate ?? DateOnly.FromDateTime(utcNow),
            StartTime = start ?? TimeOnly.FromDateTime(utcNow.AddMinutes(15)),
            DurationMinutes = 60,
            Name = "No-Gi",
        };
        context.ClassSessions.Add(session);
        await context.SaveChangesAsync();

        return (tenant, member, kid, guardianUserId, session);
    }

    [Fact]
    public async Task SelfCheckIn_InsideWindow_CreatesPending_AndIsIdempotent()
    {
        var (tenant, member, _, _, session) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var attendance = new AttendanceService(context);

        var first = await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Self, member.UserId!.Value);
        var second = await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Self, member.UserId!.Value);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(AttendanceStatus.Pending, first.Status);
        Assert.Single(await context.AttendanceRecords.Where(a => a.SessionId == session.Id).ToListAsync());
    }

    [Fact]
    public async Task SelfCheckIn_OutsideWindow_Throws_ButInstructorBackfills()
    {
        // Session far in the past: window closed.
        var (tenant, member, _, _, session) = await SeedAsync(new DateOnly(2026, 1, 5), new TimeOnly(9, 0));

        await using var context = fixture.CreateContext(tenant);
        var attendance = new AttendanceService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Self, member.UserId!.Value));

        var coach = new Person { Id = Guid.NewGuid(), FirstName = "Coach", LastName = "T", UserId = Guid.NewGuid(), Roles = PersonRoles.Instructor, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(coach);
        await context.SaveChangesAsync();

        var record = await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Instructor, coach.UserId!.Value);
        Assert.Equal(CheckInSource.Instructor, record.Source);
    }

    [Fact]
    public async Task GuardianCheckIn_RequiresLink()
    {
        var (tenant, member, kid, guardianUserId, session) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var attendance = new AttendanceService(context);

        var record = await attendance.CheckInAsync(session.Id, kid.Id, CheckInSource.Guardian, guardianUserId);
        Assert.Equal(CheckInSource.Guardian, record.Source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Guardian, guardianUserId));
    }

    [Fact]
    public async Task AutoConfirm_FiresAfterSessionEndPlusTwoHours_OnlyForDueSessions()
    {
        var (tenant, member, _, _, dueSession) = await SeedAsync(new DateOnly(2026, 7, 1), new TimeOnly(9, 0));

        await using var context = fixture.CreateContext(tenant);
        var attendance = new AttendanceService(context);

        // Backfill onto the past session (instructor path skips the window).
        var coach = new Person { Id = Guid.NewGuid(), FirstName = "Coach", LastName = "T", UserId = Guid.NewGuid(), Roles = PersonRoles.Instructor, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(coach);
        await context.SaveChangesAsync();
        await attendance.CheckInAsync(dueSession.Id, member.Id, CheckInSource.Instructor, coach.UserId!.Value);

        // A future session with a pending record must NOT confirm.
        var futureSession = new ClassSession { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), StartTime = new TimeOnly(9, 0), DurationMinutes = 60, Name = "Future" };
        context.ClassSessions.Add(futureSession);
        await context.SaveChangesAsync();
        await attendance.CheckInAsync(futureSession.Id, member.Id, CheckInSource.Instructor, coach.UserId!.Value);

        var confirmed = await attendance.ConfirmDueAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, confirmed);
        Assert.Equal(AttendanceStatus.Confirmed, (await context.AttendanceRecords.SingleAsync(a => a.SessionId == dueSession.Id)).Status);
        Assert.Equal(AttendanceStatus.Pending, (await context.AttendanceRecords.SingleAsync(a => a.SessionId == futureSession.Id)).Status);
    }

    [Fact]
    public async Task Stats_CountConfirmedOnly()
    {
        var (tenant, member, _, _, session) = await SeedAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), new TimeOnly(9, 0));

        await using var context = fixture.CreateContext(tenant);
        var attendance = new AttendanceService(context);

        var coach = new Person { Id = Guid.NewGuid(), FirstName = "Coach", LastName = "T", UserId = Guid.NewGuid(), Roles = PersonRoles.Instructor, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(coach);
        await context.SaveChangesAsync();

        var record = await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Instructor, coach.UserId!.Value);

        var before = await attendance.StatsAsync(member.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(0, before.VerifiedHours);

        await attendance.SetStatusAsync(record.Id, session.Id, AttendanceStatus.Confirmed);

        var after = await attendance.StatsAsync(member.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(1, after.VerifiedHours);
        Assert.Equal(1, after.WeeklyCounts.Sum(w => w.Count));
    }

    [Fact]
    public async Task CancellingASession_NotifiesCheckedInMembersAndGuardians()
    {
        var (tenant, member, kid, guardianUserId, session) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        context.Users.Add(new Infrastructure.Identity.AppUser { Id = member.UserId!.Value, UserName = $"m-{member.UserId:N}@x.test", Email = $"m-{member.UserId:N}@x.test" });
        context.Users.Add(new Infrastructure.Identity.AppUser { Id = guardianUserId, UserName = $"g-{guardianUserId:N}@x.test", Email = $"g-{guardianUserId:N}@x.test" });
        await context.SaveChangesAsync();

        var attendance = new AttendanceService(context);
        await attendance.CheckInAsync(session.Id, member.Id, CheckInSource.Self, member.UserId!.Value);
        await attendance.CheckInAsync(session.Id, kid.Id, CheckInSource.Guardian, guardianUserId);

        var schedule = new ScheduleService(context, new NotificationService(context));
        await schedule.CancelSessionAsync(session.Id, "Mat repairs");

        var notifications = await context.Notifications.ToListAsync();
        Assert.Contains(notifications, n => n.RecipientUserId == member.UserId);
        Assert.Contains(notifications, n => n.RecipientUserId == guardianUserId);
    }
}
