using GymStation.Domain.People;
using GymStation.Domain.Ranks;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class RankLadderTests(PostgresFixture fixture)
{
    private async Task<TenantContext> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Lad {suffix}", Slug = $"lad-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return tenant;
    }

    [Fact]
    public async Task CustomLadder_CreatesRanksMovesAndArchives_EndToEnd()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var system = await service.CreateSystemAsync("Muay Thai Prajioud");
        await service.AddRankAsync(system.Id, "White", "#F2EEE3", "#141416", 0);
        await service.AddRankAsync(system.Id, "Red", "#C4554D", "#141416", 0);
        await service.AddRankAsync(system.Id, "Blue", "#3B5DC9", "#141416", 0);

        // Move Blue above Red — the unique (system, order) index must survive the
        // swap. (Scoped to the custom system: seeded IBJJF has a Blue too.)
        var blue = await context.Ranks.SingleAsync(r => r.RankSystemId == system.Id && r.Name == "Blue");
        await service.MoveRankAsync(blue.Id, -1);

        var ordered = await context.Ranks.Where(r => r.RankSystemId == system.Id).OrderBy(r => r.Order).Select(r => r.Name).ToListAsync();
        Assert.Equal(["White", "Blue", "Red"], ordered);

        // The custom ladder awards like any other.
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Mai", LastName = "K", Roles = PersonRoles.Member, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();
        var white = await context.Ranks.SingleAsync(r => r.RankSystemId == system.Id && r.Name == "White");
        await service.RecordAwardAsync(person.Id, white.Id, 0, new DateOnly(2026, 8, 1), null, false, null);

        // A rank with awards refuses removal; the ladder archives instead and
        // vanishes from the default picker while history survives.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveRankAsync(white.Id));
        await service.SetSystemArchivedAsync(system.Id, true);
        Assert.DoesNotContain(await service.GetVisibleSystemsAsync(), s => s.Id == system.Id);
        Assert.Contains(await service.GetVisibleSystemsAsync(includeArchived: true), s => s.Id == system.Id);
        Assert.Single(await context.RankAwards.Where(a => a.PersonId == person.Id).ToListAsync());
    }

    [Fact]
    public async Task SeededLadders_AreImmutableHere()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var seeded = await context.RankSystems.FirstAsync(s => s.IsSeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameSystemAsync(seeded.Id, "Hijacked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetSystemArchivedAsync(seeded.Id, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRankAsync(seeded.Id, "Fake", "#111111", "#222222", 1));
    }

    [Fact]
    public async Task CustomLadders_AreInvisibleToOtherGyms()
    {
        var tenant = await SeedGymAsync();
        var otherTenant = await SeedGymAsync();

        await using (var context = fixture.CreateContext(tenant))
        {
            await new RankService(context).CreateSystemAsync("House Ladder");
        }

        await using (var foreign = fixture.CreateContext(otherTenant))
        {
            Assert.DoesNotContain(await new RankService(foreign).GetVisibleSystemsAsync(), s => s.Name == "House Ladder");
        }
    }
}
