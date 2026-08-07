using GymStation.Domain.Money;
using GymStation.Domain.People;
using GymStation.Infrastructure.Seeding;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

/// <summary>
/// The standard test tenant's contract (round 4.5): the counts ARE the spec —
/// flows are tested against this seed, so its shape must never drift silently.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StandardSeederTests(PostgresFixture fixture)
{
    private async Task<Guid> SeedAsync(string slug)
    {
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        return await new StandardSeeder(context, tenant).SeedAsync(slug, "Testworks Combat Club");
    }

    [Fact]
    public async Task SeedsExactlyThreeHundredPeople_WithThePinnedArrears()
    {
        var slug = $"std-{Guid.NewGuid():N}"[..20];
        var gymId = await SeedAsync(slug);

        var reader = new TenantContext();
        reader.SetGym(gymId);
        await using var db = fixture.CreateContext(reader);

        Assert.Equal(300, await db.Persons.CountAsync());

        // Exactly 14 behind: 7×1mo, 5×2mo, 2×3mo — derived from the ledger, not a flag.
        var charges = await db.Charges.ToListAsync();
        var payments = await db.Payments.ToListAsync();
        var balances = charges.GroupBy(c => c.PersonId).ToDictionary(
            g => g.Key,
            g => g.Sum(c => c.Amount) - payments.Where(p => p.PersonId == g.Key).Sum(p => p.Amount));
        var behind = balances.Where(kv => kv.Value > 0).ToList();
        Assert.Equal(14, behind.Count);

        var oneMonthAdult = 85m;
        Assert.Equal(7, behind.Count(kv => kv.Value <= oneMonthAdult));
        Assert.Equal(5, behind.Count(kv => kv.Value > oneMonthAdult && kv.Value <= 2 * oneMonthAdult));
        Assert.Equal(2, behind.Count(kv => kv.Value > 2 * oneMonthAdult));

        // Family charges carry the #181 breakdown; the over-size family bills extras.
        var familyCharges = charges.Where(c => c.CycleKey != null && c.CycleKey.Contains(":family:")).ToList();
        Assert.Equal(12, familyCharges.Count); // 4 billed families × 3 cycles
        Assert.Contains(familyCharges, c => c.Amount == 190m && c.FamilyExtraAmount == 40m); // Okonkwo over-size
        Assert.Contains(familyCharges, c => c.Amount == 180m && c.FamilyExtraAmount == 180m); // Varga per-head
        Assert.All(familyCharges, c => Assert.NotNull(c.FamilyAdults));
    }

    [Fact]
    public async Task EveryLadderRank_IsHeldBySomeone()
    {
        var slug = $"std-{Guid.NewGuid():N}"[..20];
        var gymId = await SeedAsync(slug);

        var reader = new TenantContext();
        reader.SetGym(gymId);
        await using var db = fixture.CreateContext(reader);

        var awardedRankIds = (await db.RankAwards.Select(a => a.RankId).Distinct().ToListAsync()).ToHashSet();
        var systemNames = await db.RankSystems.ToDictionaryAsync(s => s.Id, s => s.Name);
        var allRanks = await db.Ranks.ToListAsync();

        Assert.Equal(4, systemNames.Count); // IBJJF adult + kids, Prajioud, Judo
        foreach (var rank in allRanks)
        {
            Assert.True(awardedRankIds.Contains(rank.Id), $"No one holds {systemNames[rank.RankSystemId]} / {rank.Name}");
        }
    }

    [Fact]
    public async Task ArchetypesAndPathologies_AreAllPresent()
    {
        var slug = $"std-{Guid.NewGuid():N}"[..20];
        var gymId = await SeedAsync(slug);

        var reader = new TenantContext();
        reader.SetGym(gymId);
        await using var db = fixture.CreateContext(reader);

        // Staff-only desk person holds NO login (#87-clean, unlike the demo's kim).
        var desk = await db.Persons.SingleAsync(p => p.Roles == PersonRoles.Staff);
        Assert.Null(desk.UserId);

        // Visitors: flagged, plan-less.
        Assert.Equal(2, await db.Persons.CountAsync(p => p.Visitor));
        Assert.Equal(0, await db.Persons.CountAsync(p => p.Visitor && p.MembershipPlanId != null));

        // Archived members keep history but no newest-cycle charge.
        var archived = await db.Persons.Where(p => p.Archived).ToListAsync();
        Assert.Equal(2, archived.Count);
        // Derived from the seeded data, not the wall clock — a month rollover
        // mid-test must not flake this (review round).
        var newestCycle = await db.Charges
            .Where(c => c.CycleKey != null && !c.CycleKey.Contains(":family:"))
            .MaxAsync(c => c.CycleKey);
        foreach (var person in archived)
        {
            Assert.True(await db.Charges.AnyAsync(c => c.PersonId == person.Id));
            Assert.False(await db.Charges.AnyAsync(c => c.PersonId == person.Id && c.CycleKey == newestCycle));
        }

        // The dormant-pointer pathology: a member points at an ARCHIVED plan.
        var archivedPlan = await db.MembershipPlans.SingleAsync(p => p.Archived);
        Assert.True(await db.Persons.AnyAsync(p => p.MembershipPlanId == archivedPlan.Id && !p.Archived));

        // Covered-dormant: a personal-plan holder who is a member of a family
        // with an active family-scope plan.
        var coveredDormant = await db.Persons
            .Where(p => p.MembershipPlanId != null && !p.Archived && !p.Visitor)
            .Join(db.FamilyMembers, p => p.Id, m => m.PersonId, (p, m) => new { p, m.FamilyId })
            .Join(db.Families.Where(f => f.MembershipPlanId != null), x => x.FamilyId, f => f.Id, (x, f) => x.p)
            .ToListAsync();
        Assert.Contains(coveredDormant, p => p.LastName == "Feld");

        // The bills-both trap: a family plan whose primary's linked Person is NOT a member.
        var families = await db.Families.Include(f => f.Members).Include(f => f.Guardians).ToListAsync();
        var trap = families.Single(f => f.Name == "ASHFORD FAMILY");
        var primaryUser = trap.Guardians.Single(g => g.IsPrimary).GuardianUserId;
        var primaryPerson = await db.Persons.SingleAsync(p => p.UserId == primaryUser);
        Assert.DoesNotContain(trap.Members, m => m.PersonId == primaryPerson.Id);
        Assert.NotNull(primaryPerson.MembershipPlanId);

        // Leo-shape: ward on an individual plan, family without a family plan.
        var morrow = families.Single(f => f.Name == "MORROW FAMILY");
        Assert.Null(morrow.MembershipPlanId);
        var finn = await db.Persons.SingleAsync(p => p.Id == morrow.Members.Single().PersonId);
        Assert.NotNull(finn.MembershipPlanId);
        Assert.Null(finn.UserId);

        // Schedule edges: paused template, cancelled session, open sub request, one-off.
        Assert.True(await db.ClassTemplates.AnyAsync(t => !t.Active));
        Assert.True(await db.ClassSessions.AnyAsync(s => s.CancelledReason != null));
        Assert.True(await db.SubstitutionRequests.AnyAsync(r => r.ProposedSubPersonId == null));
        Assert.True(await db.ClassSessions.AnyAsync(s => s.TemplateId == null && s.CancelledReason == null));

        // Comms: unread notifications and contact messages exist on day one.
        Assert.True(await db.Notifications.CountAsync(n => n.ReadUtc == null) >= 4);
        Assert.Equal(2, await db.ContactMessages.CountAsync(m => m.ReadUtc == null));

        // Backfilled weeks carry their mint-ledger claims.
        Assert.True(await db.ClassTemplateWeeks.CountAsync() >= 12 * 20);
    }

    [Fact]
    public async Task IsDeterministic_AndRefusesAnExistingSlug()
    {
        var slugA = $"std-{Guid.NewGuid():N}"[..20];
        var slugB = $"std-{Guid.NewGuid():N}"[..20];
        var gymA = await SeedAsync(slugA);
        var gymB = await SeedAsync(slugB);

        async Task<(int Persons, int Awards, int Sessions, decimal Outstanding)> Snapshot(Guid gymId)
        {
            var reader = new TenantContext();
            reader.SetGym(gymId);
            await using var db = fixture.CreateContext(reader);
            return (
                await db.Persons.CountAsync(),
                await db.RankAwards.CountAsync(),
                await db.ClassSessions.CountAsync(),
                await db.Charges.SumAsync(c => c.Amount) - await db.Payments.SumAsync(p => p.Amount));
        }

        Assert.Equal(await Snapshot(gymA), await Snapshot(gymB));

        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new StandardSeeder(context, tenant).SeedAsync(slugA, "Again"));
    }
}
