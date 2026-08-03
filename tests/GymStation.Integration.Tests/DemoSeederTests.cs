using GymStation.Domain.Attendance;
using GymStation.Infrastructure.Seeding;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class DemoSeederTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SeedsAFullPitchTenant_AndRefusesToRunTwice()
    {
        var slug = $"demo-{Guid.NewGuid():N}"[..20];
        var tenant = new TenantContext();

        Guid gymId;
        await using (var context = fixture.CreateContext(tenant))
        {
            gymId = await new DemoSeeder(context, tenant).SeedAsync(slug, "Demo Gym");
        }

        var reader = new TenantContext();
        reader.SetGym(gymId);
        await using var db = fixture.CreateContext(reader);

        Assert.True(await db.Persons.CountAsync() >= 45);
        Assert.Equal(15, await db.ClassTemplates.CountAsync()); // 9 BJJ + 3 bootcamp + 3 Muay Thai
        Assert.True(await db.ClassSessions.CountAsync() >= 100);
        Assert.True(await db.AttendanceRecords.CountAsync(a => a.Status == AttendanceStatus.Confirmed) > 300);
        Assert.True(await db.RankAwards.CountAsync() >= 35);

        // Every kids-ladder belt is represented: Leo (grey) + one generated kid per belt.
        var kidsRankIds = await db.Ranks
            .Where(r => r.RankSystemId == GymStation.Infrastructure.Ranks.IbjjfSeed.KidsSystemId)
            .Select(r => r.Id)
            .ToListAsync();
        Assert.True(await db.RankAwards.CountAsync(a => kidsRankIds.Contains(a.RankId)) >= 13);
        // THE regression lock for #140: the tri-discipline additions must not
        // shift the shared Random sequence that tunes the ledger fiction.
        Assert.Equal(510m, (await db.Charges.SumAsync(c => c.Amount)) - await db.Payments.SumAsync(p => p.Amount));
        Assert.Equal(5, await db.Expenses.CountAsync());
        Assert.Equal(3, await db.GymEvents.CountAsync());
        Assert.Equal(5, await db.StaffProfiles.CountAsync()); // 4 coaches + Priya Kim (front desk, Staff role)

        // Tri-discipline content (#140): types, custom ladder, programs, marketing copy.
        var typeNames = await db.ClassTypes.Select(t => t.Name).ToListAsync();
        Assert.Contains("bootcamp", typeNames);
        Assert.Contains("muay-thai", typeNames);

        var prajioud = await db.RankSystems.SingleAsync(s => s.Name == "Muay Thai Prajioud");
        Assert.False(prajioud.IsSeeded);
        Assert.Equal(gymId, prajioud.GymId);
        var armbandIds = await db.Ranks.Where(r => r.RankSystemId == prajioud.Id).Select(r => r.Id).ToListAsync();
        Assert.Equal(6, armbandIds.Count);
        Assert.Equal(3, await db.RankAwards.CountAsync(a => armbandIds.Contains(a.RankId))); // Chai, Webb, Omar

        Assert.Equal(3, await db.GymPrograms.CountAsync());
        Assert.Equal(3, await db.SuccessStories.CountAsync());
        Assert.False(string.IsNullOrWhiteSpace((await db.GymSettings.SingleAsync()).AboutText));
        Assert.True(await db.ClassSessions.CountAsync(s => s.Name.StartsWith("Muay Thai")) >= 12);
        Assert.True(await db.ClassSessions.CountAsync(s => s.Name.StartsWith("Bootcamp")) >= 12);

        // Second run refuses.
        var again = new TenantContext();
        await using var context2 = fixture.CreateContext(again);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DemoSeeder(context2, again).SeedAsync(slug, "Demo Gym"));
    }
}
