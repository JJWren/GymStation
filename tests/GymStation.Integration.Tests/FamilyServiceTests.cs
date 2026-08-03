using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.People;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymStation.Integration.Tests;

/// <summary>
/// The grilled family-of-5 fiction (#89): father is PRIMARY, mother helps run the
/// family, grandparent only checks the kids in, two ward kids. Every flag is probed
/// against the action it gates.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FamilyServiceTests(PostgresFixture fixture)
{
    private sealed record Cast(
        TenantContext Tenant, Guid FamilyId,
        Guid Father, Guid Mother, Guid Grandparent,
        Guid FatherGuardianId, Guid MotherGuardianId, Guid GrandparentGuardianId,
        Guid Kid1, Guid Kid2, Guid AdultUncle);

    private async Task<Cast> SeedAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Fam {suffix}", Slug = $"fam-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        var kid1 = new Person { Id = Guid.NewGuid(), FirstName = "Tom", LastName = "Hale", JoinedOn = new DateOnly(2026, 1, 1) };
        var kid2 = new Person { Id = Guid.NewGuid(), FirstName = "Ivy", LastName = "Hale", JoinedOn = new DateOnly(2026, 1, 1) };
        var uncle = new Person { Id = Guid.NewGuid(), FirstName = "Ray", LastName = "Hale", UserId = Guid.NewGuid(), JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.AddRange(kid1, kid2, uncle);
        await context.SaveChangesAsync();

        var service = new FamilyService(context);
        var father = Guid.NewGuid();
        var mother = Guid.NewGuid();
        var grandparent = Guid.NewGuid();

        var family = await service.CreateFamilyAsync(FamilyActor.Staff, "Hale Family");
        await service.AddMemberAsync(FamilyActor.Staff, family.Id, kid1.Id, isWard: true);
        await service.AddMemberAsync(FamilyActor.Staff, family.Id, kid2.Id, isWard: true);
        await service.AddGuardianAsync(FamilyActor.Staff, family.Id, father); // first = primary, all flags
        await service.AddGuardianAsync(FamilyActor.Staff, family.Id, mother, actForWards: true, manageGuardians: true);
        await service.AddGuardianAsync(FamilyActor.Staff, family.Id, grandparent, actForWards: true);

        var guardians = await context.FamilyGuardians.Where(g => g.FamilyId == family.Id).ToListAsync();
        return new Cast(
            tenant, family.Id, father, mother, grandparent,
            guardians.Single(g => g.GuardianUserId == father).Id,
            guardians.Single(g => g.GuardianUserId == mother).Id,
            guardians.Single(g => g.GuardianUserId == grandparent).Id,
            kid1.Id, kid2.Id, uncle.Id);
    }

    [Fact]
    public async Task TheFirstGuardian_IsPrimary_AndHoldsEveryFlag()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);

        var father = await context.FamilyGuardians.SingleAsync(g => g.Id == cast.FatherGuardianId);
        Assert.True(father.IsPrimary);
        Assert.True(father.ActForWards && father.ManageGuardians && father.ManageMembers && father.ViewBilling);
    }

    [Fact]
    public async Task ActForWards_GatesActing_AndWardsListMatches()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        // Grandparent acts for both kids but nothing else; nobody acts for the adult uncle.
        Assert.True(await service.CanActForAsync(cast.Grandparent, cast.Kid1));
        Assert.True(await service.CanActForAsync(cast.Grandparent, cast.Kid2));
        Assert.Equal(2, (await service.WardsForAsync(cast.Grandparent)).Count);

        await service.AddMemberAsync(FamilyActor.Staff, cast.FamilyId, cast.AdultUncle, isWard: false);
        Assert.False(await service.CanActForAsync(cast.Father, cast.AdultUncle));

        // Turning the flag off closes the gate.
        await service.SetGuardianFlagsAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.GrandparentGuardianId,
            actForWards: false, manageGuardians: false, manageMembers: false, viewBilling: false);
        Assert.False(await service.CanActForAsync(cast.Grandparent, cast.Kid1));
        Assert.Empty(await service.WardsForAsync(cast.Grandparent));
    }

    [Fact]
    public async Task ManageMembers_GatesMembershipChanges()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        // Mother has ManageGuardians but NOT ManageMembers.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMemberAsync(FamilyActor.User(cast.Mother), cast.FamilyId, cast.AdultUncle, isWard: false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetWardAsync(FamilyActor.User(cast.Mother), cast.FamilyId, cast.Kid1, isWard: false));

        // The primary can do everything; a ward flip round-trips.
        await service.AddMemberAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.AdultUncle, isWard: false);
        await service.SetWardAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.Kid1, isWard: false);
        Assert.False((await context.FamilyMembers.SingleAsync(m => m.PersonId == cast.Kid1)).IsWard);
        await service.RemoveMemberAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.AdultUncle);
    }

    [Fact]
    public async Task ManageGuardians_GatesGuardianChanges_ButNeverThePrimary()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        // Grandparent (ActForWards only) can't touch guardians.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddGuardianAsync(FamilyActor.User(cast.Grandparent), cast.FamilyId, Guid.NewGuid()));

        // Mother can — she holds ManageGuardians.
        var aunt = Guid.NewGuid();
        await service.AddGuardianAsync(FamilyActor.User(cast.Mother), cast.FamilyId, aunt, viewBilling: true);
        Assert.Equal(4, await context.FamilyGuardians.CountAsync(g => g.FamilyId == cast.FamilyId));

        // But nobody edits or removes the primary — primacy must transfer instead.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetGuardianFlagsAsync(FamilyActor.User(cast.Mother), cast.FamilyId, cast.FatherGuardianId,
                true, true, true, true));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveGuardianAsync(FamilyActor.User(cast.Mother), cast.FamilyId, cast.FatherGuardianId));
    }

    [Fact]
    public async Task PrimacyTransfers_OnlyByPrimaryOrStaff_AndGrantsEverything()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        // Mother can't take primacy herself, even with ManageGuardians.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferPrimaryAsync(FamilyActor.User(cast.Mother), cast.FamilyId, cast.MotherGuardianId));

        // The father hands it over; the mother now holds every flag.
        await service.TransferPrimaryAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.MotherGuardianId);
        var mother = await context.FamilyGuardians.SingleAsync(g => g.Id == cast.MotherGuardianId);
        Assert.True(mother.IsPrimary);
        Assert.True(mother.ActForWards && mother.ManageGuardians && mother.ManageMembers && mother.ViewBilling);
        Assert.False((await context.FamilyGuardians.SingleAsync(g => g.Id == cast.FatherGuardianId)).IsPrimary);

        // Staff can repair primacy too (structure is exactly their power).
        await service.TransferPrimaryAsync(FamilyActor.Staff, cast.FamilyId, cast.FatherGuardianId);
        Assert.True((await context.FamilyGuardians.SingleAsync(g => g.Id == cast.FatherGuardianId)).IsPrimary);
    }

    [Fact]
    public async Task WardDiaries_FollowActingAuthority()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var families = new FamilyService(context);
        var diary = new GymStation.Infrastructure.Training.TrainingDiaryService(context, families);

        // The father writes in Kid1's diary; the entry belongs to the KID's Person.
        var entry = await diary.AddAsync(
            cast.Father, GymStation.Domain.Training.TrainingEntryKind.LessonNotes,
            new DateOnly(2026, 8, 1), null, "armbar drilling", null, [], forPersonId: cast.Kid1);
        Assert.Equal(cast.Kid1, entry.PersonId);

        // The grandparent (ActForWards) can open it; a stranger cannot write one.
        Assert.NotNull(await diary.GetEntryAsync(cast.Grandparent, entry.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => diary.AddAsync(Guid.NewGuid(), GymStation.Domain.Training.TrainingEntryKind.LessonNotes,
                new DateOnly(2026, 8, 1), null, "nope", null, [], forPersonId: cast.Kid1));

        // Turning the grandparent's ActForWards off closes the diary too.
        await families.SetGuardianFlagsAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.GrandparentGuardianId,
            actForWards: false, manageGuardians: false, manageMembers: false, viewBilling: false);
        Assert.Null(await diary.GetEntryAsync(cast.Grandparent, entry.Id));

        Assert.Single(await diary.GetMonthAsync(cast.Father, new DateOnly(2026, 8, 1), cast.Kid1));
    }

    [Fact]
    public async Task Graduation_MakesTheWardTheirOwnAdultAccount()
    {
        var cast = await SeedAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<ITenantContext>(_ => cast.Tenant);
        services.AddDbContext<GymStationDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<GymStationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        // The grandparent (not primary) can't graduate; an adult can't be graduated.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GraduateAsync(FamilyActor.User(cast.Grandparent), cast.FamilyId, cast.Kid1, "kid@example.test", users));
        await service.AddMemberAsync(FamilyActor.Staff, cast.FamilyId, cast.AdultUncle, isWard: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GraduateAsync(FamilyActor.Staff, cast.FamilyId, cast.AdultUncle, "uncle@example.test", users));

        // A ward without a login needs an email; graduation creates + links the account.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GraduateAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.Kid1, "  ", users));

        var result = await service.GraduateAsync(FamilyActor.User(cast.Father), cast.FamilyId, cast.Kid1, "tom.grad@example.test", users);
        Assert.Equal("tom.grad@example.test", result.Email);
        Assert.NotNull(result.TempPassword);

        var person = await context.Persons.AsNoTracking().SingleAsync(p => p.Id == cast.Kid1);
        Assert.NotNull(person.UserId);

        var membership = await context.FamilyMembers.AsNoTracking().SingleAsync(m => m.PersonId == cast.Kid1);
        Assert.False(membership.IsWard); // adult member: a family plan may keep covering them

        // The whole diary re-privatizes by construction: guardian authority derives
        // from IsWard, so the father can no longer act.
        Assert.False(await service.CanActForAsync(cast.Father, cast.Kid1));

        // The staff path succeeds too, and a taken email is refused loudly instead of
        // colliding on the (GymId, UserId) unique index or mislinking the ward.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GraduateAsync(FamilyActor.Staff, cast.FamilyId, cast.Kid2, "tom.grad@example.test", users));
        var staffResult = await service.GraduateAsync(FamilyActor.Staff, cast.FamilyId, cast.Kid2, "ivy.grad@example.test", users);
        Assert.NotNull(staffResult.TempPassword);
        Assert.False((await context.FamilyMembers.AsNoTracking().SingleAsync(m => m.PersonId == cast.Kid2)).IsWard);
    }

    [Fact]
    public async Task OneFamilyPerPerson_AndGuardianshipCountsAsGymMembership()
    {
        var cast = await SeedAsync();
        await using var context = fixture.CreateContext(cast.Tenant);
        var service = new FamilyService(context);

        var second = await service.CreateFamilyAsync(FamilyActor.Staff, "Second Family");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddMemberAsync(FamilyActor.Staff, second.Id, cast.Kid1, isWard: true));

        Assert.True(await service.IsGuardianInGymAsync(cast.Grandparent));
        Assert.False(await service.IsGuardianInGymAsync(Guid.NewGuid()));
    }
}
