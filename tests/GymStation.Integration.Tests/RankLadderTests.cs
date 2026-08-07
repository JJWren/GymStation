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

        // Rank-level operations must hit the same wall through the rank's system.
        var seededRank = await context.Ranks.FirstAsync(r => r.RankSystemId == seeded.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRankAsync(seededRank.Id, "Hijacked", "#111111", "#222222", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveRankAsync(seededRank.Id, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveRankAsync(seededRank.Id));
    }

    [Fact]
    public async Task DisciplineMapping_LabelsAnyVisibleLadder_PerGym()
    {
        var tenant = await SeedGymAsync();
        var otherTenant = await SeedGymAsync();

        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var bjj = new GymStation.Domain.Marketing.GymProgram { Id = Guid.NewGuid(), Title = "BJJ" };
        var retired = new GymStation.Domain.Marketing.GymProgram { Id = Guid.NewGuid(), Title = "Old", Archived = true };
        context.GymPrograms.AddRange(bjj, retired);
        await context.SaveChangesAsync();

        // A SEEDED (platform-shared) ladder takes this gym's label — mapping is
        // gym-owned, so seeded immutability doesn't apply (ADR 0006).
        var seeded = await context.RankSystems.FirstAsync(s => s.IsSeeded);
        await service.SetSystemProgramAsync(seeded.Id, bjj.Id);

        var custom = await service.CreateSystemAsync("House Ladder");
        var labels = await service.GetDisciplineLabelsAsync();
        Assert.Equal("BJJ", labels[seeded.Id]);
        Assert.Equal("House Ladder", labels[custom.Id]); // unmapped → falls back to the ladder name

        // Re-map is an upsert; clearing restores the fallback.
        await service.SetSystemProgramAsync(seeded.Id, bjj.Id);
        await service.SetSystemProgramAsync(seeded.Id, null);
        labels = await service.GetDisciplineLabelsAsync();
        Assert.Equal(seeded.Name, labels[seeded.Id]);

        // Archived programs can't take new links.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetSystemProgramAsync(custom.Id, retired.Id));

        // Another gym neither sees this mapping nor can borrow this gym's program.
        await service.SetSystemProgramAsync(seeded.Id, bjj.Id);
        await using var foreign = fixture.CreateContext(otherTenant);
        var foreignService = new RankService(foreign);
        var foreignLabels = await foreignService.GetDisciplineLabelsAsync();
        Assert.Equal(seeded.Name, foreignLabels[seeded.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreignService.SetSystemProgramAsync(seeded.Id, bjj.Id));
    }

    [Fact]
    public async Task PrimaryDiscipline_LeadsCompactRank_WithFallbacks()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "M", Roles = PersonRoles.Member, JoinedOn = new DateOnly(2024, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        // Two disciplines: an older BJJ blue, then a newer Judo yellow.
        var seeded = await context.RankSystems.FirstAsync(s => s.IsSeeded && s.Name.Contains("Adult"));
        var blue = await context.Ranks.FirstAsync(r => r.RankSystemId == seeded.Id && r.Name == "Blue");
        var judo = await service.CreateSystemAsync("Judo");
        await service.AddRankAsync(judo.Id, "Yellow", "#D9A62E", "#141416", 0);
        var yellow = await context.Ranks.SingleAsync(r => r.RankSystemId == judo.Id);
        await service.RecordAwardAsync(person.Id, blue.Id, 2, new DateOnly(2024, 5, 1), null, false, null);
        await service.RecordAwardAsync(person.Id, yellow.Id, 0, new DateOnly(2026, 2, 1), null, false, null);

        // Default: latest award wins.
        var current = (await service.GetPrimaryRanksAsync([person.Id]))[person.Id];
        Assert.Equal(yellow.Id, current.Rank.Id);

        // Explicit primary overrides recency; clearing restores it.
        await service.SetPrimaryRankSystemAsync(person.Id, seeded.Id);
        current = (await service.GetPrimaryRanksAsync([person.Id]))[person.Id];
        Assert.Equal(blue.Id, current.Rank.Id);

        await service.SetPrimaryRankSystemAsync(person.Id, null);
        current = (await service.GetPrimaryRanksAsync([person.Id]))[person.Id];
        Assert.Equal(yellow.Id, current.Rank.Id);

        // A primary the person holds no rank in falls back rather than blanking.
        var empty = await service.CreateSystemAsync("Empty Ladder");
        await service.SetPrimaryRankSystemAsync(person.Id, empty.Id);
        current = (await service.GetPrimaryRanksAsync([person.Id]))[person.Id];
        Assert.Equal(yellow.Id, current.Rank.Id);

        // Unknown ladders refuse.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPrimaryRankSystemAsync(person.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAward_RecomputesCurrentAndKeepsAuditRow()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var person = new Person { Id = Guid.NewGuid(), FirstName = "Del", LastName = "R", Roles = PersonRoles.Member, JoinedOn = new DateOnly(2024, 1, 1) };
        var staff = new Person { Id = Guid.NewGuid(), FirstName = "Staff", LastName = "R", Roles = PersonRoles.Admin, JoinedOn = new DateOnly(2020, 1, 1) };
        context.Persons.AddRange(person, staff);
        await context.SaveChangesAsync();

        var seeded = await context.RankSystems.FirstAsync(s => s.IsSeeded && s.Name.Contains("Adult"));
        var white = await context.Ranks.FirstAsync(r => r.RankSystemId == seeded.Id && r.Name == "White");
        var blue = await context.Ranks.FirstAsync(r => r.RankSystemId == seeded.Id && r.Name == "Blue");
        await service.RecordAwardAsync(person.Id, white.Id, 0, new DateOnly(2024, 2, 1), null, false, null);
        var blueAward = await service.RecordAwardAsync(person.Id, blue.Id, 0, new DateOnly(2026, 3, 1), null, false, null);

        // Wrong entry removed: current falls back to the earlier belt...
        await service.DeleteAwardAsync(blueAward.Id, staff.Id);
        var current = (await service.GetPrimaryRanksAsync([person.Id]))[person.Id];
        Assert.Equal(white.Id, current.Rank.Id);
        Assert.DoesNotContain(await service.GetAwardsForPersonAsync(person.Id), a => a.Id == blueAward.Id);

        // ...the audit row persists (who + when), invisible except past the filter...
        var audit = await context.RankAwards.IgnoreQueryFilters().SingleAsync(a => a.Id == blueAward.Id);
        Assert.NotNull(audit.DeletedUtc);
        Assert.Equal(staff.Id, audit.DeletedByPersonId);

        // ...and a second delete of the same award refuses (already removed).
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAwardAsync(blueAward.Id, staff.Id));
    }

    [Fact]
    public async Task RetiredRank_LeavesPickersButKeepsHistory()
    {
        var tenant = await SeedGymAsync();
        await using var context = fixture.CreateContext(tenant);
        var service = new RankService(context);

        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ret", LastName = "R", Roles = PersonRoles.Member, JoinedOn = new DateOnly(2024, 1, 1) };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        var system = await service.CreateSystemAsync("House Ladder");
        await service.AddRankAsync(system.Id, "Green Sash", "#3E8E5A", "#141416", 0);
        var sash = await context.Ranks.SingleAsync(r => r.RankSystemId == system.Id);
        await service.RecordAwardAsync(person.Id, sash.Id, 0, new DateOnly(2026, 1, 5), null, false, null);

        // Held ranks refuse deletion — soft-deleted history counts too — and
        // retire instead: no NEW awards, history intact.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveRankAsync(sash.Id));
        await service.SetRankRetiredAsync(sash.Id, true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordAwardAsync(person.Id, sash.Id, 0, new DateOnly(2026, 2, 1), null, false, null));
        Assert.Single(await service.GetAwardsForPersonAsync(person.Id));

        // Unretire restores awardability; seeded ladders refuse rank retirement.
        await service.SetRankRetiredAsync(sash.Id, false);
        await service.RecordAwardAsync(person.Id, sash.Id, 0, new DateOnly(2026, 2, 1), null, false, null);
        var seededRank = await context.Ranks.FirstAsync(r => context.RankSystems.Any(s => s.Id == r.RankSystemId && s.IsSeeded));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRankRetiredAsync(seededRank.Id, true));
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
