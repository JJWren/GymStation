using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class TenantIsolationTests(PostgresFixture fixture)
{
    private async Task<(Gym GymA, Gym GymB)> SeedTwoGymsAsync()
    {
        // Gyms are the tenants themselves — not tenant-filtered, created without an active tenant.
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymA = new Gym { Id = Guid.NewGuid(), Name = $"Gym A {suffix}", Slug = $"gym-a-{suffix}", TimeZoneId = "America/Chicago" };
        var gymB = new Gym { Id = Guid.NewGuid(), Name = $"Gym B {suffix}", Slug = $"gym-b-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.AddRange(gymA, gymB);
        await setup.SaveChangesAsync();
        return (gymA, gymB);
    }

    private async Task<Person> SeedPersonAsync(Guid gymId, string first, string last, Guid? userId = null)
    {
        var tenant = new TenantContext();
        tenant.SetGym(gymId);
        await using var context = fixture.CreateContext(tenant);
        var person = new Person { Id = Guid.NewGuid(), FirstName = first, LastName = last, UserId = userId, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();
        return person;
    }

    [Fact]
    public async Task QueryFilter_HidesOtherTenantsCompletely()
    {
        var (gymA, gymB) = await SeedTwoGymsAsync();
        var personA = await SeedPersonAsync(gymA.Id, "Ana", "Reyes");
        await SeedPersonAsync(gymB.Id, "Blake", "Otten");

        var tenantA = new TenantContext();
        tenantA.SetGym(gymA.Id);
        await using var context = fixture.CreateContext(tenantA);

        var visible = await context.Persons.Where(p => p.Id == personA.Id || p.LastName == "Otten").ToListAsync();

        Assert.Single(visible);
        Assert.Equal(personA.Id, visible[0].Id);
    }

    [Fact]
    public async Task QueryFilter_NoActiveTenant_SeesNothing()
    {
        var (gymA, _) = await SeedTwoGymsAsync();
        await SeedPersonAsync(gymA.Id, "Ana", "Reyes");

        await using var context = fixture.CreateContext();

        Assert.Empty(await context.Persons.ToListAsync());
        Assert.Empty(await context.GymSettings.ToListAsync());
        Assert.Empty(await context.GuardianLinks.ToListAsync());
    }

    [Fact]
    public async Task WriteGuard_AssignsActiveTenantToNewRows()
    {
        var (gymA, _) = await SeedTwoGymsAsync();

        var tenantA = new TenantContext();
        tenantA.SetGym(gymA.Id);
        await using var context = fixture.CreateContext(tenantA);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Tom", LastName = "Hale", JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        Assert.Equal(gymA.Id, person.GymId);
    }

    [Fact]
    public async Task WriteGuard_BlocksCrossTenantWrites()
    {
        var (gymA, gymB) = await SeedTwoGymsAsync();

        var tenantA = new TenantContext();
        tenantA.SetGym(gymA.Id);
        await using var context = fixture.CreateContext(tenantA);
        context.Persons.Add(new Person { Id = Guid.NewGuid(), GymId = gymB.Id, FirstName = "Mallory", LastName = "Cross", JoinedOn = new DateOnly(2026, 1, 1) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Cross-tenant write blocked", ex.Message);
    }

    [Fact]
    public async Task WriteGuard_RequiresActiveTenantForTenantOwnedRows()
    {
        await using var context = fixture.CreateContext();
        context.Persons.Add(new Person { Id = Guid.NewGuid(), FirstName = "No", LastName = "Tenant", JoinedOn = new DateOnly(2026, 1, 1) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("requires an active tenant", ex.Message);
    }

    [Fact]
    public async Task GymMembership_ListsEveryGymForAUser_AcrossTenants()
    {
        var (gymA, gymB) = await SeedTwoGymsAsync();
        var userId = Guid.NewGuid();

        await using (var setup = fixture.CreateContext())
        {
            setup.Users.Add(new AppUser { Id = userId, UserName = $"ana-{userId:N}@example.test", Email = $"ana-{userId:N}@example.test" });
            await setup.SaveChangesAsync();
        }

        await SeedPersonAsync(gymA.Id, "Ana", "Reyes", userId);
        await SeedPersonAsync(gymB.Id, "Ana", "Reyes", userId);

        await using var context = fixture.CreateContext();
        var memberships = await new GymMembershipService(context).GetGymsForUserAsync(userId);

        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, m => m.GymId == gymA.Id);
        Assert.Contains(memberships, m => m.GymId == gymB.Id);
    }

    [Fact]
    public async Task GuardianLinks_AreTenantScoped()
    {
        var (gymA, gymB) = await SeedTwoGymsAsync();
        var child = await SeedPersonAsync(gymA.Id, "Leo", "Park");
        var guardianUserId = Guid.NewGuid();

        var tenantA = new TenantContext();
        tenantA.SetGym(gymA.Id);
        await using (var context = fixture.CreateContext(tenantA))
        {
            context.GuardianLinks.Add(new GuardianLink { Id = Guid.NewGuid(), GuardianUserId = guardianUserId, ChildPersonId = child.Id });
            await context.SaveChangesAsync();
        }

        var tenantB = new TenantContext();
        tenantB.SetGym(gymB.Id);
        await using var contextB = fixture.CreateContext(tenantB);

        Assert.Empty(await contextB.GuardianLinks.Where(l => l.GuardianUserId == guardianUserId).ToListAsync());
    }
}
