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
