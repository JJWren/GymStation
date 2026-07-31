using GymStation.Domain.Ranks;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Ranks;

public record PersonRankSummary(Guid PersonId, CurrentRank? Primary);

public class RankService(GymStationDbContext db)
{
    public Task<List<RankSystem>> GetVisibleSystemsAsync(CancellationToken ct = default)
    {
        return db.RankSystems
            .Include(s => s.Ranks.OrderBy(r => r.Order))
            .OrderBy(s => s.GymId == null ? 0 : 1).ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Primary (most recently awarded) current rank per person, for roster belt bars.
    /// A person's full per-system history lives on their profile.
    /// </summary>
    public async Task<Dictionary<Guid, CurrentRank>> GetPrimaryRanksAsync(IReadOnlyCollection<Guid> personIds, CancellationToken ct = default)
    {
        var awards = await db.RankAwards
            .Where(a => personIds.Contains(a.PersonId))
            .Include(a => a.Rank)
            .ToListAsync(ct);

        return awards
            .GroupBy(a => a.PersonId)
            .Select(g =>
            {
                var latestSystem = g
                    .OrderBy(a => a.AwardedOn).ThenBy(a => a.RecordedUtc)
                    .Last().Rank.RankSystemId;
                var current = RankProgress.Current(g.Where(a => a.Rank.RankSystemId == latestSystem));
                return (g.Key, current);
            })
            .Where(x => x.current is not null)
            .ToDictionary(x => x.Key, x => x.current!);
    }

    public async Task<List<RankAward>> GetAwardsForPersonAsync(Guid personId, CancellationToken ct = default)
    {
        return await db.RankAwards
            .Where(a => a.PersonId == personId)
            .Include(a => a.Rank)
            .OrderByDescending(a => a.AwardedOn).ThenByDescending(a => a.RecordedUtc)
            .ToListAsync(ct);
    }

    public async Task<RankAward> RecordAwardAsync(
        Guid personId,
        Guid rankId,
        int stripes,
        DateOnly awardedOn,
        Guid? awardedByPersonId,
        bool selfReported,
        string? note,
        CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        var rank = await db.Ranks.SingleOrDefaultAsync(r => r.Id == rankId, ct)
            ?? throw new InvalidOperationException("Rank not found in a ladder visible to this gym.");

        if (stripes < 0 || stripes > rank.MaxStripes)
        {
            throw new InvalidOperationException($"{rank.Name} allows 0–{rank.MaxStripes} stripes; got {stripes}.");
        }

        var award = new RankAward
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            RankId = rank.Id,
            Stripes = stripes,
            AwardedOn = awardedOn,
            AwardedByPersonId = awardedByPersonId,
            SelfReported = selfReported,
            Note = note,
        };

        db.RankAwards.Add(award);
        await db.SaveChangesAsync(ct);
        award.Rank = rank;
        return award;
    }
}
