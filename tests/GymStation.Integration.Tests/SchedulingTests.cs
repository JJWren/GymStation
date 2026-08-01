using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Scheduling;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class SchedulingTests(PostgresFixture fixture)
{
    private static readonly DateOnly Monday = new(2026, 8, 3); // a Monday

    private async Task<(Gym Gym, TenantContext Tenant, Person Coach, Person Sub, Person Admin)> SeedGymAsync(
        SubstitutionMode mode = SubstitutionMode.AutoApply, bool openClaims = true)
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Sched {suffix}", Slug = $"sched-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        context.GymSettings.Add(new GymSettings { GymId = gym.Id, SubstitutionMode = mode, OpenClaimsEnabled = openClaims });

        Person MakePerson(string first, PersonRoles roles) => new()
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = "Test",
            Roles = roles,
            UserId = Guid.NewGuid(),
            JoinedOn = new DateOnly(2026, 1, 1),
        };

        var coach = MakePerson("Coach", PersonRoles.Instructor | PersonRoles.Member);
        var sub = MakePerson("Sub", PersonRoles.Instructor);
        var admin = MakePerson("Admin", PersonRoles.Admin | PersonRoles.Owner);
        context.Persons.AddRange(coach, sub, admin);

        foreach (var person in new[] { coach, sub, admin })
        {
            context.Users.Add(new Infrastructure.Identity.AppUser
            {
                Id = person.UserId!.Value,
                UserName = $"{person.FirstName}-{person.UserId:N}@example.test",
                Email = $"{person.FirstName}-{person.UserId:N}@example.test",
            });
        }

        await context.SaveChangesAsync();
        return (gym, tenant, coach, sub, admin);
    }

    private async Task<ClassSession> SeedWeekWithTemplateAsync(TenantContext tenant, Guid instructorPersonId)
    {
        await using var context = fixture.CreateContext(tenant);
        context.ClassTemplates.Add(new ClassTemplate
        {
            Id = Guid.NewGuid(),
            Name = "No-Gi",
            Day = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 90,
            DefaultInstructorPersonId = instructorPersonId,
        });
        await context.SaveChangesAsync();

        var schedule = new ScheduleService(context, new NotificationService(context));
        var sessions = await schedule.GetWeekAsync(Monday);
        return sessions.Single(s => s.Name == "No-Gi");
    }

    [Fact]
    public async Task Materialization_IsLazy_AndIdempotent()
    {
        var (_, tenant, coach, _, _) = await SeedGymAsync();
        await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));

        var again = await schedule.GetWeekAsync(Monday);

        Assert.Single(again, s => s.Name == "No-Gi");
        Assert.Equal(Monday.AddDays(1), again.Single(s => s.Name == "No-Gi").Date);
        Assert.Equal(coach.Id, again.Single(s => s.Name == "No-Gi").InstructorPersonId);
    }

    [Fact]
    public async Task CancelSession_SetsStateAndNotifiesStaffAndInstructor()
    {
        var (_, tenant, coach, _, admin) = await SeedGymAsync();
        var session = await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        await schedule.CancelSessionAsync(session.Id, "Flooded mats");

        var reloaded = await context.ClassSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Cancelled, reloaded.Status);

        var notifications = await context.Notifications.ToListAsync();
        Assert.Contains(notifications, n => n.RecipientUserId == admin.UserId && n.Category == NotificationCategory.SessionCancelled);
        Assert.Contains(notifications, n => n.RecipientUserId == coach.UserId);
        Assert.All(notifications, n => Assert.Single(n.Deliveries, d => d.Channel == DeliveryChannel.Email));
    }

    [Fact]
    public async Task AutoApplyGym_AcceptFlipsSessionInstructor()
    {
        var (_, tenant, coach, sub, _) = await SeedGymAsync(SubstitutionMode.AutoApply);
        var session = await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var subs = new SubstitutionService(context, new NotificationService(context));

        var request = await subs.RequestAsync(session.Id, coach.Id, null, "sick");
        await subs.AcceptAsync(request.Id, sub.Id);

        var reloaded = await context.ClassSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(sub.Id, reloaded.InstructorPersonId);
        Assert.Equal(SubstitutionStatus.Applied, (await context.SubstitutionRequests.SingleAsync(r => r.Id == request.Id)).Status);
    }

    [Fact]
    public async Task AdminGateGym_RequiresApprovalBeforeScheduleChanges()
    {
        var (_, tenant, coach, sub, _) = await SeedGymAsync(SubstitutionMode.AdminGate);
        var session = await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var subs = new SubstitutionService(context, new NotificationService(context));

        var request = await subs.RequestAsync(session.Id, coach.Id, sub.Id, null);
        await subs.AcceptAsync(request.Id, sub.Id);

        Assert.Equal(coach.Id, (await context.ClassSessions.SingleAsync(s => s.Id == session.Id)).InstructorPersonId);

        await subs.ApproveAsync(request.Id);

        Assert.Equal(sub.Id, (await context.ClassSessions.SingleAsync(s => s.Id == session.Id)).InstructorPersonId);
    }

    [Fact]
    public async Task Escalation_MarksUnfilledRequestsInsideTwentyFourHours()
    {
        var (_, tenant, coach, _, admin) = await SeedGymAsync();
        var session = await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var subs = new SubstitutionService(context, new NotificationService(context));
        var request = await subs.RequestAsync(session.Id, coach.Id, null, null);

        // Derive the UTC instant from the gym's zone so DST can never skew the test.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var localBefore = session.Date.ToDateTime(session.StartTime).AddHours(-12);
        var nowUtc = new DateTimeOffset(localBefore, zone.GetUtcOffset(localBefore)).ToUniversalTime();

        var escalated = await subs.EscalateDueAsync(nowUtc);

        Assert.Equal(1, escalated);
        Assert.NotNull((await context.SubstitutionRequests.SingleAsync(r => r.Id == request.Id)).EscalatedUtc);
        Assert.Contains(await context.Notifications.ToListAsync(),
            n => n.Category == NotificationCategory.SwapEscalated && n.RecipientUserId == admin.UserId);

        // Second pass is a no-op.
        Assert.Equal(0, await subs.EscalateDueAsync(nowUtc.AddMinutes(10)));
    }

    [Fact]
    public async Task ReopenSession_RestoresScheduledAndNotifies()
    {
        var (_, tenant, coach, _, _) = await SeedGymAsync();
        var session = await SeedWeekWithTemplateAsync(tenant, coach.Id);

        await using var context = fixture.CreateContext(tenant);
        var schedule = new ScheduleService(context, new NotificationService(context));
        await schedule.CancelSessionAsync(session.Id, "Flooded mats");
        await schedule.ReopenSessionAsync(session.Id);

        var reloaded = await context.ClassSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Scheduled, reloaded.Status);
        Assert.Null(reloaded.CancelledReason);
        Assert.Contains(await context.Notifications.ToListAsync(), n => n.Title.StartsWith("Back on:"));

        // Reopening a session that isn't cancelled is a no-op.
        await schedule.ReopenSessionAsync(session.Id);
        Assert.Equal(SessionStatus.Scheduled, (await context.ClassSessions.SingleAsync(s => s.Id == session.Id)).Status);
    }
}
