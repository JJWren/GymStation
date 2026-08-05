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
        await service.UpdateAsync(member.Id, "Dara", "Nair-Smith", new DateOnly(2015, 3, 9), PersonRoles.Member | PersonRoles.Instructor, visitor: false);

        var reloaded = await context.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Equal("Nair-Smith", reloaded.LastName);
        Assert.Equal(new DateOnly(2015, 3, 9), reloaded.DateOfBirth);
        Assert.Equal(PersonRoles.Member | PersonRoles.Instructor, reloaded.Roles);
    }

    [Fact]
    public async Task TheLastActiveOwner_CannotLoseOwnerOrBeArchived()
    {
        var (tenant, owner, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        var demote = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(owner.Id, "Jordan", "Torres", null, PersonRoles.Admin | PersonRoles.Member, visitor: false));
        Assert.Contains("only active Owner", demote.Message);

        var archive = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetArchivedAsync(owner.Id, true));
        Assert.Contains("only active Owner", archive.Message);

        // With a second Owner in place, both operations go through.
        await service.UpdateAsync(member.Id, "Dara", "Nair", null, PersonRoles.Member | PersonRoles.Owner, visitor: false);
        await service.UpdateAsync(owner.Id, "Jordan", "Torres", null, PersonRoles.Admin | PersonRoles.Member, visitor: false);
        Assert.False((await context.Persons.SingleAsync(p => p.Id == owner.Id)).Roles.HasFlag(PersonRoles.Owner));
    }

    [Fact]
    public async Task Visitors_AreQuickAdded_AndConvertByClearingTheFlag()
    {
        var (tenant, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddVisitorAsync(" ", "Passerby"));

        var visitor = await service.AddVisitorAsync("  Sam ", "Passerby");
        var stored = await context.Persons.SingleAsync(p => p.Id == visitor.Id);
        Assert.True(stored.Visitor);
        Assert.Equal("Sam", stored.FirstName);
        Assert.Equal(PersonRoles.Member, stored.Roles);
        Assert.Null(stored.MembershipPlanId); // no plan — the monthly cycle never charges them

        // Conversion: clear the flag through the normal edit path.
        await service.UpdateAsync(visitor.Id, "Sam", "Passerby", null, PersonRoles.Member, visitor: false);
        Assert.False((await context.Persons.SingleAsync(p => p.Id == visitor.Id)).Visitor);
    }

    [Fact]
    public async Task SetName_TrimsValidatesAndLeavesEverythingElseAlone()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetNameAsync(member.Id, " ", "Nair"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetNameAsync(member.Id, new string('a', 81), "Nair"));

        await service.SetNameAsync(member.Id, "  Dara ", " Nair-Smith ");
        var stored = await context.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Equal("Dara", stored.FirstName);
        Assert.Equal("Nair-Smith", stored.LastName);
        Assert.Equal(PersonRoles.Member, stored.Roles); // untouched — names only (#128)
    }

    [Fact]
    public async Task UpdateWithContact_IsAtomic_ARejectedPhoneRollsRolesBack()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        // The 31-char phone is rejected AFTER the roles write inside the same
        // transaction — nothing may stick.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateWithContactAsync(
            member.Id, new DateOnly(2000, 1, 1), PersonRoles.Member | PersonRoles.Instructor,
            visitor: false, phoneNumber: new string('9', 31), smsAllowed: true));

        await using var fresh = fixture.CreateContext(tenant);
        var stored = await fresh.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Equal(PersonRoles.Member, stored.Roles);
        Assert.Null(stored.DateOfBirth);
        Assert.Null(stored.PhoneNumber);

        // And the happy path lands both halves together.
        await service.UpdateWithContactAsync(
            member.Id, new DateOnly(2000, 1, 1), PersonRoles.Member | PersonRoles.Instructor,
            visitor: false, phoneNumber: "555 010 0100", smsAllowed: true);
        stored = await fresh.Persons.AsNoTracking().SingleAsync(p => p.Id == member.Id);
        Assert.Equal(PersonRoles.Member | PersonRoles.Instructor, stored.Roles);
        Assert.Equal("555 010 0100", stored.PhoneNumber);
        Assert.True(stored.SmsAllowed);
    }

    [Fact]
    public async Task Contact_SetsTrimsAndClears_WithConsentTiedToTheNumber()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetContactAsync(member.Id, new string('9', 31), true));

        await service.SetContactAsync(member.Id, "  +1 555 010 0100 ", true);
        var stored = await context.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Equal("+1 555 010 0100", stored.PhoneNumber);
        Assert.True(stored.SmsAllowed);

        // Clearing the number always clears consent with it.
        await service.SetContactAsync(member.Id, "  ", true);
        stored = await context.Persons.SingleAsync(p => p.Id == member.Id);
        Assert.Null(stored.PhoneNumber);
        Assert.False(stored.SmsAllowed);
    }

    [Fact]
    public async Task StaffOnly_CannotKeepALogin()
    {
        var (tenant, _, member) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        // member (seeded with a UserId? — Dara has none; give her one)
        var person = await context.Persons.SingleAsync(p => p.Id == member.Id);
        person.UserId = Guid.NewGuid();
        await context.SaveChangesAsync();

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(member.Id, "Dara", "Nair", null, PersonRoles.Staff, visitor: false));
        Assert.Contains("Staff-only", blocked.Message);

        // Staff alongside a portal-capable role is fine, and staff-only without a login is fine.
        await service.UpdateAsync(member.Id, "Dara", "Nair", null, PersonRoles.Staff | PersonRoles.Member, visitor: false);
        person.UserId = null;
        await context.SaveChangesAsync();
        await service.UpdateAsync(member.Id, "Dara", "Nair", null, PersonRoles.Staff, visitor: false);
        Assert.Equal(PersonRoles.Staff, (await context.Persons.SingleAsync(p => p.Id == member.Id)).Roles);
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

    [Fact]
    public async Task AssignPlan_GuardsScope_AndConvertsVisitors()
    {
        var (tenant, _, _) = await SeedAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        var perPerson = new GymStation.Domain.Money.MembershipPlan { Id = Guid.NewGuid(), Name = "Adult", Price = 85m };
        var familyScope = new GymStation.Domain.Money.MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Family",
            Price = 150m,
            Scope = GymStation.Domain.Money.PlanScope.Family,
        };
        context.MembershipPlans.AddRange(perPerson, familyScope);
        var visitor = new Person { Id = Guid.NewGuid(), FirstName = "Walk", LastName = "In", Visitor = true, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(visitor);
        await context.SaveChangesAsync();

        // Family-scope plans attach to a FAMILY, never a person (#196).
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignPlanAsync(visitor.Id, familyScope.Id));

        // Assigning a real plan converts the visitor — the documented moment.
        await service.AssignPlanAsync(visitor.Id, perPerson.Id);
        var converted = await context.Persons.SingleAsync(p => p.Id == visitor.Id);
        Assert.False(converted.Visitor);
        Assert.Equal(perPerson.Id, converted.MembershipPlanId);

        // Clearing the plan never re-flags.
        await service.AssignPlanAsync(visitor.Id, null);
        var cleared = await context.Persons.SingleAsync(p => p.Id == visitor.Id);
        Assert.False(cleared.Visitor);
        Assert.Null(cleared.MembershipPlanId);
    }

    [Fact]
    public async Task LinkLogin_RoundTrips_WithEveryGuard()
    {
        var (tenant, _, _) = await SeedAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new PersonService(context);

        var person = new Person { Id = Guid.NewGuid(), FirstName = "Lia", LastName = "Chen", JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        var userId = Guid.NewGuid();
        await service.LinkLoginAsync(person.Id, userId);
        Assert.Equal(userId, (await context.Persons.SingleAsync(p => p.Id == person.Id)).UserId);

        // Already linked → refuse; unlink → relink round-trips (#191).
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkLoginAsync(person.Id, Guid.NewGuid()));
        await service.UnlinkLoginAsync(person.Id);
        Assert.Null((await context.Persons.SingleAsync(p => p.Id == person.Id)).UserId);
        await service.LinkLoginAsync(person.Id, userId);

        // Same (GymId, UserId) space as graduation's taken-email guard (#92).
        var second = new Person { Id = Guid.NewGuid(), FirstName = "Rob", LastName = "Chen", JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(second);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkLoginAsync(second.Id, userId));

        // Staff-only grants no portal (#87 parity).
        var deskOnly = new Person { Id = Guid.NewGuid(), FirstName = "Pat", LastName = "Desk", Roles = PersonRoles.Staff, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(deskOnly);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkLoginAsync(deskOnly.Id, Guid.NewGuid()));
    }
}
