using GymStation.Domain.Notifications;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

/// <summary>
/// The inbox filters (#182): read chips, ILIKE title search with wildcard
/// escaping, and gym-local DAY/RANGE windows over CreatedUtc.
/// </summary>
[Collection(PostgresCollection.Name)]
public class NotificationTests(PostgresFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 8, 20);

    private async Task<(TenantContext Tenant, Guid UserId)> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Inbox {suffix}", Slug = $"inbox-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return (tenant, Guid.NewGuid());
    }

    private static Notification Note(Guid userId, string title, DateTimeOffset createdUtc, bool read = false) => new()
    {
        Id = Guid.NewGuid(),
        RecipientUserId = userId,
        Category = NotificationCategory.SessionChanged,
        Title = title,
        Body = "body",
        CreatedUtc = createdUtc,
        ReadUtc = read ? createdUtc.AddMinutes(5) : null,
    };

    private static TimeZoneInfo Chicago => TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    [Fact]
    public async Task ReadChips_UnreadIsTheDefault()
    {
        var (tenant, userId) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        context.Notifications.AddRange(
            Note(userId, "Changed: Gi", new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)),
            Note(userId, "Changed: No-Gi", new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)),
            Note(userId, "Cover needed: Kids", new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero), read: true));
        await context.SaveChangesAsync();

        var scope = context.Notifications.Where(n => n.RecipientUserId == userId);

        var unreadDefault = NotificationFilters.Resolve(null, null, null, null, null, null, Today);
        Assert.False(unreadDefault.Narrowed);
        Assert.Equal(2, await NotificationFilters.Apply(scope, unreadDefault, Chicago).CountAsync());

        var read = NotificationFilters.Resolve("read", null, null, null, null, null, Today);
        Assert.True(read.Narrowed);
        Assert.Equal(1, await NotificationFilters.Apply(scope, read, Chicago).CountAsync());

        var all = NotificationFilters.Resolve("all", null, null, null, null, null, Today);
        Assert.Equal(3, await NotificationFilters.Apply(scope, all, Chicago).CountAsync());

        // Junk read values fall back to the default view.
        Assert.Equal("unread", NotificationFilters.Resolve("banana", null, null, null, null, null, Today).Read);
    }

    [Fact]
    public async Task TitleSearch_IsCaseInsensitive_AndEscapesWildcards()
    {
        var (tenant, userId) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        context.Notifications.AddRange(
            Note(userId, "Cover needed: Adv Gi", new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)),
            Note(userId, "Dues raised: 100% Effort Pass", new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)),
            Note(userId, "Dues raised: 100 Club", new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        var scope = context.Notifications.Where(n => n.RecipientUserId == userId);

        var cover = NotificationFilters.Resolve("all", "cover NEEDED", null, null, null, null, Today);
        Assert.Equal(1, await NotificationFilters.Apply(scope, cover, Chicago).CountAsync());

        // "%" in the query is a literal, not a wildcard — "100%" must not match "100 Club".
        var percent = NotificationFilters.Resolve("all", "100%", null, null, null, null, Today);
        var hits = await NotificationFilters.Apply(scope, percent, Chicago).ToListAsync();
        Assert.Single(hits);
        Assert.Contains("Effort", hits[0].Title);
    }

    [Fact]
    public async Task DayWindow_IsGymLocal()
    {
        var (tenant, userId) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        // 2026-08-04T03:00Z is 22:00 on AUG 3 in Chicago (CDT, UTC-5).
        var lateEveningLocal = Note(userId, "Changed: Late", new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
        var middayUtc = Note(userId, "Changed: Midday", new DateTimeOffset(2026, 8, 4, 17, 0, 0, TimeSpan.Zero));
        context.Notifications.AddRange(lateEveningLocal, middayUtc);
        await context.SaveChangesAsync();

        var scope = context.Notifications.Where(n => n.RecipientUserId == userId);

        var aug3 = NotificationFilters.Resolve("all", null, "day", "2026-08-03", null, null, Today);
        var aug3Hits = await NotificationFilters.Apply(scope, aug3, Chicago).ToListAsync();
        Assert.Single(aug3Hits);
        Assert.Equal(lateEveningLocal.Id, aug3Hits[0].Id);

        var aug4 = NotificationFilters.Resolve("all", null, "day", "2026-08-04", null, null, Today);
        var aug4Hits = await NotificationFilters.Apply(scope, aug4, Chicago).ToListAsync();
        Assert.Single(aug4Hits);
        Assert.Equal(middayUtc.Id, aug4Hits[0].Id);
    }

    [Fact]
    public async Task Range_SwapsWhenReversed_AndIsInclusiveOnBothEnds()
    {
        var (tenant, userId) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        context.Notifications.AddRange(
            Note(userId, "Changed: Before", new DateTimeOffset(2026, 7, 31, 17, 0, 0, TimeSpan.Zero)),
            Note(userId, "Changed: First", new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero)),
            Note(userId, "Changed: Last", new DateTimeOffset(2026, 8, 10, 17, 0, 0, TimeSpan.Zero)),
            Note(userId, "Changed: After", new DateTimeOffset(2026, 8, 11, 17, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        var scope = context.Notifications.Where(n => n.RecipientUserId == userId);

        // Reversed on purpose — Resolve swaps to [Aug 1, Aug 10].
        var range = NotificationFilters.Resolve("all", null, "range", null, "2026-08-10", "2026-08-01", Today);
        Assert.Equal("range", range.DateMode);
        Assert.Equal(new DateOnly(2026, 8, 1), range.From);
        Assert.Equal(new DateOnly(2026, 8, 10), range.To);

        var titles = await NotificationFilters.Apply(scope, range, Chicago).Select(n => n.Title).ToListAsync();
        Assert.Equal(2, titles.Count);
        Assert.Contains("Changed: First", titles);
        Assert.Contains("Changed: Last", titles);
    }

    [Fact]
    public void DateModes_FallBackToAll_WithoutValidDates()
    {
        Assert.Equal("all", NotificationFilters.Resolve(null, null, "day", null, null, null, Today).DateMode);
        Assert.Equal("all", NotificationFilters.Resolve(null, null, "day", "08/03/2026", null, null, Today).DateMode);
        Assert.Equal("all", NotificationFilters.Resolve(null, null, "range", null, "2026-08-01", null, Today).DateMode);
        // Future dates clamp to today.
        Assert.Equal(Today, NotificationFilters.Resolve(null, null, "day", "2030-01-01", null, null, Today).On);
    }
}
