using System.Security.Claims;
using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.People;
using GymStation.Infrastructure.Tenancy;
using GymStation.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

/// <summary>
/// The #217 security boundary: capability grants and the policy handler that
/// enforces them. The handler is the real gate — nav hiding is cosmetics.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PermissionTests(PostgresFixture fixture)
{
    private async Task<TenantContext> SeedGymAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Perm {suffix}", Slug = $"perm-{suffix}", TimeZoneId = "America/Chicago" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);
        return tenant;
    }

    private static Person NewPerson(PersonRoles roles, Guid? userId = null) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Test",
        LastName = Guid.NewGuid().ToString("N")[..8],
        Roles = roles,
        JoinedOn = new DateOnly(2026, 1, 1),
        UserId = userId,
    };

    private static ClaimsPrincipal PrincipalFor(Guid userId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));

    private static async Task<bool> Authorized(GymStation.Infrastructure.GymStationDbContext db, ITenantContext tenant, Guid userId, GymCapability capability)
    {
        var requirement = new CapabilityRequirement(capability);
        var context = new AuthorizationHandlerContext([requirement], PrincipalFor(userId), null);
        await new CapabilityHandler(db, tenant).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task CapabilityHandler_OwnerImplicit_GrantsForOthers_MembersNever()
    {
        var tenant = await SeedGymAsync();
        await using var db = fixture.CreateContext(tenant);

        var owner = NewPerson(PersonRoles.Owner | PersonRoles.Admin, Guid.NewGuid());
        var admin = NewPerson(PersonRoles.Admin, Guid.NewGuid());
        var coach = NewPerson(PersonRoles.Instructor | PersonRoles.Member, Guid.NewGuid());
        var member = NewPerson(PersonRoles.Member, Guid.NewGuid());
        db.Persons.AddRange(owner, admin, coach, member);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        await service.SetForPersonAsync(admin.Id, [GymCapability.ManageRoster]);
        await service.SetForPersonAsync(coach.Id, [GymCapability.ManageRanks]);

        // Owners pass everything with zero rows.
        Assert.True(await Authorized(db, tenant, owner.UserId!.Value, GymCapability.ViewFinances));

        // Admins pass exactly what they hold.
        Assert.True(await Authorized(db, tenant, admin.UserId!.Value, GymCapability.ManageRoster));
        Assert.False(await Authorized(db, tenant, admin.UserId!.Value, GymCapability.ViewFinances));

        // Instructors can hold grants without the Admin role.
        Assert.True(await Authorized(db, tenant, coach.UserId!.Value, GymCapability.ManageRanks));
        Assert.False(await Authorized(db, tenant, coach.UserId!.Value, GymCapability.ManageRoster));

        // A grant row on a plain member is dead weight, never access: the
        // handler re-checks staff-ish-ness on every evaluation.
        db.PermissionGrants.Add(new PermissionGrant { Id = Guid.NewGuid(), GymId = db.CurrentGymId!.Value, PersonId = member.Id, Capability = GymCapability.ManageRoster });
        await db.SaveChangesAsync();
        Assert.False(await Authorized(db, tenant, member.UserId!.Value, GymCapability.ManageRoster));
    }

    [Fact]
    public async Task PermissionService_ReplacesGuardsAndPresets()
    {
        var tenant = await SeedGymAsync();
        await using var db = fixture.CreateContext(tenant);

        var admin = NewPerson(PersonRoles.Admin, Guid.NewGuid());
        var owner = NewPerson(PersonRoles.Owner, Guid.NewGuid());
        var member = NewPerson(PersonRoles.Member, Guid.NewGuid());
        db.Persons.AddRange(admin, owner, member);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Replace semantics: the set IS the grant list.
        await service.SetForPersonAsync(admin.Id, [GymCapability.ManageRoster, GymCapability.ManageSchedule]);
        await service.SetForPersonAsync(admin.Id, [GymCapability.ManageSchedule, GymCapability.ViewReports]);
        Assert.Equal(
            new HashSet<GymCapability> { GymCapability.ManageSchedule, GymCapability.ViewReports },
            await service.GetForPersonAsync(admin.Id));

        // Presets are bundles, not magic.
        await service.ApplyPresetAsync(admin.Id, "front-desk");
        Assert.Equal([.. CapabilityPresets.All["front-desk"]], await service.GetForPersonAsync(admin.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyPresetAsync(admin.Id, "no-such-preset"));

        // Members can't take grants; owners have nothing to grant.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetForPersonAsync(member.Id, [GymCapability.ManageRoster]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetForPersonAsync(owner.Id, [GymCapability.ManageRoster]));

        // GetForUserAsync reports owners as all-capable — nav code never special-cases.
        Assert.Equal(Enum.GetValues<GymCapability>().Length, (await service.GetForUserAsync(owner.UserId!.Value)).Count);
    }
}
