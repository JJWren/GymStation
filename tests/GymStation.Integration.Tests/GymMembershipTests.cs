using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class GymMembershipTests(PostgresFixture fixture)
{
    private async Task<(Gym Gym, TenantContext Tenant)> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Mem {suffix}", Slug = $"mem-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return (gym, tenant);
    }

    private static Infrastructure.Identity.AppUser MakeUser(Guid id) => new()
    {
        Id = id,
        UserName = $"user-{id:N}@example.test",
        Email = $"user-{id:N}@example.test",
    };

    [Fact]
    public async Task GuardianWithNoRosterRecord_BelongsToTheChildsGym()
    {
        var (gym, tenant) = await SeedGymAsync();
        var guardianUserId = Guid.NewGuid();

        await using (var context = fixture.CreateContext(tenant))
        {
            context.Users.Add(MakeUser(guardianUserId));
            var child = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Tom",
                LastName = "Kid",
                Roles = PersonRoles.Member,
                JoinedOn = new DateOnly(2026, 1, 1),
            };
            context.Persons.Add(child);
            var family = new Family { Id = Guid.NewGuid(), Name = "TEST FAMILY" };
            context.Families.Add(family);
            context.FamilyMembers.Add(new FamilyMember { Id = Guid.NewGuid(), FamilyId = family.Id, PersonId = child.Id, IsWard = true });
            context.FamilyGuardians.Add(new FamilyGuardian
            {
                Id = Guid.NewGuid(),
                FamilyId = family.Id,
                GuardianUserId = guardianUserId,
                IsPrimary = true,
                ActForWards = true,
                ManageGuardians = true,
                ManageMembers = true,
                ViewBilling = true,
            });
            await context.SaveChangesAsync();
        }

        // Login-time context: no tenant is active yet.
        await using var reader = fixture.CreateContext();
        var memberships = new GymMembershipService(reader);

        Assert.Contains(await memberships.GetGymsForUserAsync(guardianUserId), m => m.GymId == gym.Id);
        Assert.True(await memberships.IsUserInGymAsync(guardianUserId, gym.Id));
        Assert.Equal("/schedule", await memberships.LandingPathAsync(guardianUserId, gym.Id));
    }

    [Fact]
    public async Task LandingPath_RoutesStaffToAdminAndEveryoneElseToSchedule()
    {
        var (gym, tenant) = await SeedGymAsync();
        var ownerUserId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var instructorUserId = Guid.NewGuid();

        await using (var context = fixture.CreateContext(tenant))
        {
            context.Users.AddRange(MakeUser(ownerUserId), MakeUser(memberUserId), MakeUser(instructorUserId));
            context.Persons.AddRange(
                new Person { Id = Guid.NewGuid(), FirstName = "Owner", LastName = "T", Roles = PersonRoles.Owner | PersonRoles.Admin, UserId = ownerUserId, JoinedOn = new DateOnly(2026, 1, 1) },
                new Person { Id = Guid.NewGuid(), FirstName = "Member", LastName = "T", Roles = PersonRoles.Member, UserId = memberUserId, JoinedOn = new DateOnly(2026, 1, 1) },
                new Person { Id = Guid.NewGuid(), FirstName = "Coach", LastName = "T", Roles = PersonRoles.Instructor | PersonRoles.Member, UserId = instructorUserId, JoinedOn = new DateOnly(2026, 1, 1) });
            await context.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext();
        var memberships = new GymMembershipService(reader);

        Assert.Equal("/", await memberships.LandingPathAsync(ownerUserId, gym.Id));
        Assert.Equal("/schedule", await memberships.LandingPathAsync(memberUserId, gym.Id));

        // Instructors land on their teaching view (#39).
        Assert.Equal("/teach", await memberships.LandingPathAsync(instructorUserId, gym.Id));
    }
}
