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

        // IgnoreQueryFilters: soft-deleted awards still reference the rank (the
        // FK is Restrict) — a "successful" delete would fail at SaveChanges.
        // RankId is globally unique, so dropping the tenant filter is exact.
        if (await db.RankAwards.IgnoreQueryFilters().AnyAsync(a => a.RankId == rankId, ct))
        {
            throw new InvalidOperationException("That rank has awards on record — history stays. Retire it instead: it leaves the pickers while history keeps rendering.");
        }

        db.Ranks.Remove(rank);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Retire/unretire a rank (#220): held ranks can't be deleted, so
    /// they leave the pickers instead while every display keeps rendering them.
    /// Editable (custom, unseeded) ladders only, like every ladder mutation.</summary>
    public async Task SetRankRetiredAsync(Guid rankId, bool retired, CancellationToken ct = default)
    {
        var rank = await db.Ranks.SingleOrDefaultAsync(r => r.Id == rankId, ct)
            ?? throw new InvalidOperationException("Rank not found in a ladder visible to this gym.");
        await LoadEditableSystemAsync(rank.RankSystemId, ct);

        rank.Retired = retired;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Soft-deletes one award (#220) — a data-entry correction. Current
    /// rank recomputes from what remains; the row stays as the audit trail.
    /// Returns the award's PersonId so callers can land on the right person.</summary>
    public async Task<Guid> DeleteAwardAsync(Guid awardId, Guid? deletedByPersonId, CancellationToken ct = default)
    {
        var award = await db.RankAwards.SingleOrDefaultAsync(a => a.Id == awardId, ct)
            ?? throw new InvalidOperationException("Award not found in the active gym (or already removed).");

        award.DeletedUtc = DateTimeOffset.UtcNow;
        award.DeletedByPersonId = deletedByPersonId;
        await db.SaveChangesAsync(ct);
        return award.PersonId;
    }

    // ---- Discipline mapping (#214, ADR 0006). A gym labels any visible ladder —
    // platform IBJJF or its own — with one of its Programs. The link is gym-owned,
    // so mapping a SEEDED ladder is fine: nothing on the ladder itself changes. ----

    /// <summary>RankSystemId → discipline label for every system visible to the
    /// active gym: the linked Program's title, or the ladder's own name when the
    /// gym hasn't mapped it. One dictionary labels any rank display.</summary>
    public async Task<Dictionary<Guid, string>> GetDisciplineLabelsAsync(CancellationToken ct = default)
    {
        var labels = await db.RankSystems
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var linked = await db.RankSystemProgramLinks
            .Join(db.GymPrograms, l => l.GymProgramId, p => p.Id, (l, p) => new { l.RankSystemId, p.Title })
            .ToListAsync(ct);
        foreach (var link in linked)
        {
            labels[link.RankSystemId] = link.Title;
        }

        return labels;
    }

    /// <summary>RankSystemId → linked GymProgramId, for the mapping picker.</summary>
    public Task<Dictionary<Guid, Guid>> GetProgramLinksAsync(CancellationToken ct = default)
    {
        return db.RankSystemProgramLinks.ToDictionaryAsync(l => l.RankSystemId, l => l.GymProgramId, ct);
    }

    /// <summary>Unarchived Programs of the active gym, for the mapping picker.</summary>
    public Task<List<GymStation.Domain.Marketing.GymProgram>> GetMappableProgramsAsync(CancellationToken ct = default)
    {
        return db.GymPrograms
            .Where(p => !p.Archived)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToListAsync(ct);
    }

    public async Task SetSystemProgramAsync(Guid systemId, Guid? programId, CancellationToken ct = default)
    {
        var gymId = db.CurrentGymId
            ?? throw new InvalidOperationException("No active gym.");

        // Visibility, not editability: the tenant filter admits platform ladders
        // and this gym's own — exactly the set a gym may label.
        _ = await db.RankSystems.SingleOrDefaultAsync(s => s.Id == systemId, ct)
            ?? throw new InvalidOperationException("Ladder not found in the active gym.");

        var link = await db.RankSystemProgramLinks.SingleOrDefaultAsync(l => l.RankSystemId == systemId, ct);
        if (programId is null)
        {
            if (link is not null)
            {
                db.RankSystemProgramLinks.Remove(link);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        var program = await db.GymPrograms.SingleOrDefaultAsync(p => p.Id == programId, ct)
            ?? throw new InvalidOperationException("Program not found in the active gym.");
        if (program.Archived)
        {
            throw new InvalidOperationException("Archived programs can't label a ladder — unarchive it first.");
        }

        if (link is null)
        {
            db.RankSystemProgramLinks.Add(new RankSystemProgramLink
            {
                Id = Guid.NewGuid(),
                GymId = gymId,
                RankSystemId = systemId,
                GymProgramId = program.Id,
            });
        }
        else
        {
            link.GymProgramId = program.Id;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Primary current rank per person, for roster belt bars: the person's chosen
    /// primary discipline (#215) when they hold a rank there, else the most
    /// recently awarded system. A person's full per-system history lives on
    /// their profile.
    /// </summary>
    public async Task<Dictionary<Guid, CurrentRank>> GetPrimaryRanksAsync(IReadOnlyCollection<Guid> personIds, CancellationToken ct = default)
    {
        var awards = await db.RankAwards
            .Where(a => personIds.Contains(a.PersonId))
            .Include(a => a.Rank)
            .ToListAsync(ct);

        var preferred = await db.Persons
            .Where(p => personIds.Contains(p.Id) && p.PrimaryRankSystemId != null)
            .ToDictionaryAsync(p => p.Id, p => p.PrimaryRankSystemId!.Value, ct);

        return awards
            .GroupBy(a => a.PersonId)
            .Select(g =>
            {
                var system = preferred.TryGetValue(g.Key, out var chosen) && g.Any(a => a.Rank.RankSystemId == chosen)
                    ? chosen
                    : g.OrderBy(a => a.AwardedOn).ThenBy(a => a.RecordedUtc).Last().Rank.RankSystemId;
                var current = RankProgress.Current(g.Where(a => a.Rank.RankSystemId == system));
                return (g.Key, current);
            })
            .Where(x => x.current is not null)
            .ToDictionary(x => x.Key, x => x.current!);
    }

    /// <summary>Current rank per person WITHIN one system — the roster's
    /// discipline-scoped filter (#219). Absent key = no rank in that discipline.</summary>
    public async Task<Dictionary<Guid, CurrentRank>> GetCurrentRanksInSystemAsync(IReadOnlyCollection<Guid> personIds, Guid rankSystemId, CancellationToken ct = default)
    {
        var awards = await db.RankAwards
            .Where(a => personIds.Contains(a.PersonId) && a.Rank.RankSystemId == rankSystemId)
            .Include(a => a.Rank)
            .ToListAsync(ct);

        return awards
            .GroupBy(a => a.PersonId)
            .Select(g => (g.Key, Current: RankProgress.Current(g)))
            .Where(x => x.Current is not null)
            .ToDictionary(x => x.Key, x => x.Current!);
    }

    /// <summary>Sets (or clears, with null) a person's primary discipline. Authority
    /// — self on the member portal, staff on the admin side — is the endpoint's job.</summary>
    public async Task SetPrimaryRankSystemAsync(Guid personId, Guid? rankSystemId, CancellationToken ct = default)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && !p.Archived, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        if (rankSystemId is { } systemId
            && !await db.RankSystems.AnyAsync(s => s.Id == systemId, ct))
        {
            throw new InvalidOperationException("Ladder not found in the active gym.");
        }

        person.PrimaryRankSystemId = rankSystemId;
        await db.SaveChangesAsync(ct);
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

        if (rank.Retired)
        {
            throw new InvalidOperationException($"{rank.Name} is retired — it takes no new awards (existing history stands).");
        }

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
