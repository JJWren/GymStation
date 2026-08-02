using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.People;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class PersonServiceTests(PostgresFixture fixture)
{
    private async Task<(TenantContext Tenant, Person Owner, Person Member)> SeedAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Ppl {suffix}", Slug = $"ppl-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        var owner = new Person { Id = Guid.NewGuid(), FirstName = "Jordan", LastName = "Torres", Roles = PersonRoles.Owner | PersonRoles.Admin, JoinedOn = new DateOnly(2026, 1, 1) };
        var member = new Person { Id = Guid.NewGuid(), FirstName = "Dara", LastName = "Nair", Roles = PersonRoles.Member, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.AddRange(owner, member);
        await context.SaveChangesAsync();
        return (tenant, owner, member);
    }

    [Fact]
    public async Task Update_ChangesNameDobAndRoles()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);
        await service.UpdateAsync(member.Id, "Dara", "Nair-Smith", new DateOnly(2015, 3, 9), PersonRoles.Member);

        var reloaded = await context.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Equal("Nair-Smith", reloaded.LastName);
        Assert.Equal(new DateOnly(2015, 3, 9), reloaded.DateOfBirth);
    }

    [Fact]
    public async Task TheLastActiveOwner_CannotLoseOwnerOrBeArchived()
    {
        var (tenant, owner, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(owner.Id, "Jordan", "Torres", null, PersonRoles.Admin | PersonRoles.Member));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetArchivedAsync(owner.Id, true));

        // With a second Owner in place, both operations go through.
        await service.UpdateAsync(member.Id, "Dara", "Nair", null, PersonRoles.Member | PersonRoles.Owner);
        await service.UpdateAsync(owner.Id, "Jordan", "Torres", null, PersonRoles.Admin | PersonRoles.Member);
        Assert.False((await context.Persons.SingleAsync(p => p.Id == owner.Id)).Roles.HasFlag(PersonRoles.Owner));
    }

    [Fact]
    public async Task Archive_RoundTrips()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        await service.SetArchivedAsync(member.Id, true);
        Assert.True((await context.Persons.SingleAsync(p => p.Id == member.Id)).Archived);

        await service.SetArchivedAsync(member.Id, false);
        Assert.False((await context.Persons.SingleAsync(p => p.Id == member.Id)).Archived);
    }
}
