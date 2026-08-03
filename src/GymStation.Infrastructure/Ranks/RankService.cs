using GymStation.Domain.Ranks;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Ranks;

public record PersonRankSummary(Guid PersonId, CurrentRank? Primary);

public class RankService(GymStationDbContext db)
{
    public Task<List<RankSystem>> GetVisibleSystemsAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        return db.RankSystems
            .Where(s => includeArchived || !s.Archived)
            .Include(s => s.Ranks.OrderBy(r => r.Order))
            .OrderBy(s => s.GymId == null ? 0 : 1).ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    // ---- Custom-ladder management (#139). The glossary promised "Gyms can
    // define custom systems" since v1 — this is that promise, kept. Seeded
    // platform ladders are immutable here by construction. ----

    private static void ValidateHex(string value, string label)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$"))
        {
            throw new InvalidOperationException($"{label} must be a #rrggbb color.");
        }
    }

    private async Task<RankSystem> LoadEditableSystemAsync(Guid systemId, CancellationToken ct)
    {
        var system = await db.RankSystems
            .Include(s => s.Ranks.OrderBy(r => r.Order))
            .SingleOrDefaultAsync(s => s.Id == systemId, ct)
            ?? throw new InvalidOperationException("Ladder not found in the active gym.");

        if (system.IsSeeded || system.GymId is null)
        {
            throw new InvalidOperationException("Platform-seeded ladders can't be edited — create a custom ladder instead.");
        }

        return system;
    }

    public async Task<RankSystem> CreateSystemAsync(string name, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 80)
        {
            throw new InvalidOperationException("A ladder name is required (80 characters max).");
        }

        var gymId = db.CurrentGymId
            ?? throw new InvalidOperationException("No active gym.");

        var system = new RankSystem { Id = Guid.NewGuid(), GymId = gymId, Name = trimmed };
        db.RankSystems.Add(system);
        await db.SaveChangesAsync(ct);
        return system;
    }

    public async Task RenameSystemAsync(Guid systemId, string name, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 80)
        {
            throw new InvalidOperationException("A ladder name is required (80 characters max).");
        }

        var system = await LoadEditableSystemAsync(systemId, ct);
        system.Name = trimmed;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetSystemArchivedAsync(Guid systemId, bool archived, CancellationToken ct = default)
    {
        var system = await LoadEditableSystemAsync(systemId, ct);
        system.Archived = archived;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddRankAsync(Guid systemId, string name, string bandColorHex, string barColorHex, int maxStripes, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 60)
        {
            throw new InvalidOperationException("A rank name is required (60 characters max).");
        }

        ValidateHex(bandColorHex, "Band color");
        ValidateHex(barColorHex, "Bar color");
        if (maxStripes is < 0 or > 10)
        {
            throw new InvalidOperationException("Max stripes must be 0–10.");
        }

        var system = await LoadEditableSystemAsync(systemId, ct);
        db.Ranks.Add(new Rank
        {
            Id = Guid.NewGuid(),
            RankSystemId = system.Id,
            Name = trimmed,
            Order = (system.Ranks.Count == 0 ? 0 : system.Ranks.Max(r => r.Order)) + 1,
            BandColorHex = bandColorHex,
            BarColorHex = barColorHex,
            MaxStripes = maxStripes,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRankAsync(Guid rankId, string name, string bandColorHex, string barColorHex, int maxStripes, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 60)
        {
            throw new InvalidOperationException("A rank name is required (60 characters max).");
        }

        ValidateHex(bandColorHex, "Band color");
        ValidateHex(barColorHex, "Bar color");
        if (maxStripes is < 0 or > 10)
        {
            throw new InvalidOperationException("Max stripes must be 0–10.");
        }

        var rank = await db.Ranks.SingleOrDefaultAsync(r => r.Id == rankId, ct)
            ?? throw new InvalidOperationException("Rank not found in a ladder visible to this gym.");
        await LoadEditableSystemAsync(rank.RankSystemId, ct);

        rank.Name = trimmed;
        rank.BandColorHex = bandColorHex;
        rank.BarColorHex = barColorHex;
        rank.MaxStripes = maxStripes;
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveRankAsync(Guid rankId, int direction, CancellationToken ct = default)
    {
        if (direction is not (-1 or 1))
        {
            throw new InvalidOperationException("Direction must be one step.");
        }

        var rank = await db.Ranks.SingleOrDefaultAsync(r => r.Id == rankId, ct)
            ?? throw new InvalidOperationException("Rank not found in a ladder visible to this gym.");
        var system = await LoadEditableSystemAsync(rank.RankSystemId, ct);

        var ordered = system.Ranks.OrderBy(r => r.Order).ToList();
        var index = ordered.FindIndex(r => r.Id == rankId);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return; // edge — clamp silently, like every other mover
        }

        // (RankSystemId, Order) is UNIQUE and Postgres checks per statement — a
        // naive swap collides mid-batch. Park the run in negative space first,
        // then stamp the final order, all in one transaction.
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = -(i + 1);
        }

        await db.SaveChangesAsync(ct);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveRankAsync(Guid rankId, CancellationToken ct = default)
    {
        var rank = await db.Ranks.SingleOrDefaultAsync(r => r.Id == rankId, ct)
            ?? throw new InvalidOperationException("Rank not found in a ladder visible to this gym.");
        await LoadEditableSystemAsync(rank.RankSystemId, ct);

        if (await db.RankAwards.AnyAsync(a => a.RankId == rankId, ct))
        {
            throw new InvalidOperationException("That rank has awards on record — history stays. Archive the ladder instead.");
        }

        db.Ranks.Remove(rank);
        await db.SaveChangesAsync(ct);
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
