using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class RankTests(PostgresFixture fixture)
{
    private async Task<(Gym Gym, TenantContext Tenant)> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Rank Gym {suffix}", Slug = $"rank-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return (gym, tenant);
    }

    [Fact]
    public async Task SeededIbjjfLadders_AreVisibleToEveryTenant_AndWithoutOne()
    {
        var (_, tenant) = await SeedGymAsync();

        await using (var context = fixture.CreateContext(tenant))
        {
            var systems = await context.RankSystems.Include(s => s.Ranks).ToListAsync();
            var adult = systems.Single(s => s.Id == IbjjfSeed.AdultSystemId);
            var kids = systems.Single(s => s.Id == IbjjfSeed.KidsSystemId);

            // 5 core belts + the 7th–9th degree red belts; kids: White + the 12 grade belts.
            Assert.Equal(8, adult.Ranks.Count);
            Assert.Equal(13, kids.Ranks.Count);
            Assert.Equal(6, adult.Ranks.Single(r => r.Name == "Black").MaxStripes);
            Assert.Equal(0, adult.Ranks.Single(r => r.Name == "Red").MaxStripes);
            Assert.Equal(0, kids.Ranks.Single(r => r.Name == "White").Order);
        }

        await using (var noTenant = fixture.CreateContext())
        {
            Assert.Equal(2, await noTenant.RankSystems.CountAsync(s => s.IsSeeded));
        }
    }

    [Fact]
    public async Task CustomLadder_IsInvisibleToOtherGyms()
    {
        var (gymA, tenantA) = await SeedGymAsync();
        var (_, tenantB) = await SeedGymAsync();

        Guid customId;
        await using (var contextA = fixture.CreateContext(tenantA))
        {
            var custom = new GymStation.Domain.Ranks.RankSystem { Id = Guid.NewGuid(), GymId = gymA.Id, Name = "House Judo" };
            contextA.RankSystems.Add(custom);
            await contextA.SaveChangesAsync();
            customId = custom.Id;
        }

        await using var contextB = fixture.CreateContext(tenantB);
        Assert.Null(await contextB.RankSystems.SingleOrDefaultAsync(s => s.Id == customId));
    }

    [Fact]
    public async Task RecordAward_ValidatesStripeRange()
    {
        var (_, tenant) = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Reyes", JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        var service = new RankService(context);
        var purple = await context.Ranks.SingleAsync(r => r.RankSystemId == IbjjfSeed.AdultSystemId && r.Name == "Purple");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordAwardAsync(person.Id, purple.Id, 5, new DateOnly(2026, 7, 1), null, false, null));
        Assert.Contains("0–4", ex.Message);

        var award = await service.RecordAwardAsync(person.Id, purple.Id, 2, new DateOnly(2026, 7, 1), null, false, null);
        Assert.Equal(tenant.CurrentGymId, award.GymId);
    }

    [Fact]
    public async Task Awards_AreTenantScoped()
    {
        var (_, tenantA) = await SeedGymAsync();
        var (_, tenantB) = await SeedGymAsync();

        await using (var contextA = fixture.CreateContext(tenantA))
        {
            var person = new Person { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Reyes", JoinedOn = new DateOnly(2026, 1, 1) };
            contextA.Persons.Add(person);
            await contextA.SaveChangesAsync();

            var white = await contextA.Ranks.SingleAsync(r => r.RankSystemId == IbjjfSeed.AdultSystemId && r.Name == "White");
            await new RankService(contextA).RecordAwardAsync(person.Id, white.Id, 0, new DateOnly(2026, 1, 2), null, false, null);
        }

        await using var contextB = fixture.CreateContext(tenantB);
        Assert.Empty(await contextB.RankAwards.ToListAsync());
    }
}
