using GymStation.Domain.Marketing;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class ProgramTests(PostgresFixture fixture)
{
    private async Task<TenantContext> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Prog {suffix}", Slug = $"prog-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return tenant;
    }

    [Fact]
    public async Task Programs_AreTenantScoped_AndPublicQueryOrdersActives()
    {
        var tenant = await SeedGymAsync();
        var otherTenant = await SeedGymAsync();

        await using (var context = fixture.CreateContext(tenant))
        {
            context.GymPrograms.AddRange(
                new GymProgram { Id = Guid.NewGuid(), Title = "Muay Thai", SortOrder = 2 },
                new GymProgram { Id = Guid.NewGuid(), Title = "BJJ", SortOrder = 1 },
                new GymProgram { Id = Guid.NewGuid(), Title = "Retired", SortOrder = 0, Archived = true });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext(tenant))
        {
            // The public page's exact query shape: actives only, admin order.
            var visible = await context.GymPrograms
                .Where(p => !p.Archived)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                .Select(p => p.Title)
                .ToListAsync();
            Assert.Equal(["BJJ", "Muay Thai"], visible);
        }

        // The other gym sees nothing — a missed filter here is a data leak.
        await using (var foreign = fixture.CreateContext(otherTenant))
        {
            Assert.Empty(await foreign.GymPrograms.ToListAsync());
        }
    }

    [Fact]
    public async Task Stories_AreTenantScoped_AndPublicQueryOrdersActives()
    {
        var tenant = await SeedGymAsync();
        var otherTenant = await SeedGymAsync();

        await using (var context = fixture.CreateContext(tenant))
        {
            context.SuccessStories.AddRange(
                new Domain.Marketing.SuccessStory { Id = Guid.NewGuid(), Body = "Second", SortOrder = 2 },
                new Domain.Marketing.SuccessStory { Id = Guid.NewGuid(), Body = "First", AttributedTo = "Sam O.", SortOrder = 1 },
                new Domain.Marketing.SuccessStory { Id = Guid.NewGuid(), Body = "Hidden", SortOrder = 0, Archived = true });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext(tenant))
        {
            var visible = await context.SuccessStories
                .Where(s => !s.Archived)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => s.Body)
                .ToListAsync();
            Assert.Equal(["First", "Second"], visible);
        }

        await using (var foreign = fixture.CreateContext(otherTenant))
        {
            Assert.Empty(await foreign.SuccessStories.ToListAsync());
        }
    }
}
