using GymStation.Domain.Training;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Training;

public record TwoTierHours(int VerifiedHours, int SelfReportedHours)
{
    public int TotalHours => VerifiedHours + SelfReportedHours;
}

/// <summary>
/// The ONLY read/write path for diary entries, and every method scopes to the requesting
/// user's own Person. There is deliberately no API that takes an arbitrary person id:
/// diaries are private to their author (Q14/H fold) and no staff surface can reach them.
/// </summary>
public class TrainingDiaryService(GymStationDbContext db)
{
    private async Task<Guid?> OwnPersonIdAsync(Guid requestingUserId, CancellationToken ct)
    {
        return (await db.Persons.SingleOrDefaultAsync(p => p.UserId == requestingUserId && !p.Archived, ct))?.Id;
    }

    public async Task<List<TrainingEntry>> GetMineAsync(Guid requestingUserId, int take = 30, CancellationToken ct = default)
    {
        if (await OwnPersonIdAsync(requestingUserId, ct) is not { } personId)
        {
            return [];
        }

        return await db.TrainingEntries
            .Where(e => e.PersonId == personId)
            .Include(e => e.Rolls)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>One month of the caller's own entries, for the diary calendar view.</summary>
    public async Task<List<TrainingEntry>> GetMonthAsync(Guid requestingUserId, DateOnly monthStart, CancellationToken ct = default)
    {
        if (await OwnPersonIdAsync(requestingUserId, ct) is not { } personId)
        {
            return [];
        }

        var next = monthStart.AddMonths(1);
        return await db.TrainingEntries
            .Where(e => e.PersonId == personId && e.EntryDate >= monthStart && e.EntryDate < next)
            .Include(e => e.Rolls)
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedUtc)
            .ToListAsync(ct);
    }

    /// <summary>A single entry, or null when it doesn't exist or belongs to someone else.</summary>
    public async Task<TrainingEntry?> GetEntryAsync(Guid requestingUserId, Guid entryId, CancellationToken ct = default)
    {
        if (await OwnPersonIdAsync(requestingUserId, ct) is not { } personId)
        {
            return null;
        }

        return await db.TrainingEntries
            .Where(e => e.Id == entryId && e.PersonId == personId)
            .Include(e => e.Rolls)
            .SingleOrDefaultAsync(ct);
    }

    private async Task ValidateAsync(TrainingEntryKind kind, int? selfReportedMinutes, Guid? sessionId, CancellationToken ct)
    {
        if (kind == TrainingEntryKind.SelfReported && selfReportedMinutes is not > 0)
        {
            throw new InvalidOperationException("Self-reported entries need a positive number of minutes.");
        }

        if (sessionId is { } sid && !await db.ClassSessions.AnyAsync(s => s.Id == sid, ct))
        {
            throw new InvalidOperationException("Linked session not found in the active gym.");
        }
    }

    private static List<TrainingRoll> BuildRolls(
        Guid entryId, IReadOnlyList<(Guid? PartnerPersonId, string PartnerLabel, string? Summary)> rolls)
    {
        return rolls
            .Where(r => !string.IsNullOrWhiteSpace(r.PartnerLabel))
            .Select(r => new TrainingRoll
            {
                Id = Guid.NewGuid(),
                TrainingEntryId = entryId,
                PartnerPersonId = r.PartnerPersonId,
                PartnerLabel = r.PartnerLabel.Trim(),
                Summary = r.Summary,
            })
            .ToList();
    }

    public async Task<TrainingEntry> AddAsync(
        Guid requestingUserId,
        TrainingEntryKind kind,
        DateOnly entryDate,
        Guid? sessionId,
        string? notes,
        int? selfReportedMinutes,
        IReadOnlyList<(Guid? PartnerPersonId, string PartnerLabel, string? Summary)> rolls,
        CancellationToken ct = default)
    {
        var personId = await OwnPersonIdAsync(requestingUserId, ct)
            ?? throw new InvalidOperationException("You have no roster record in the active gym.");

        await ValidateAsync(kind, selfReportedMinutes, sessionId, ct);

        var entry = new TrainingEntry
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            Kind = kind,
            EntryDate = entryDate,
            SessionId = sessionId,
            Notes = notes,
            SelfReportedMinutes = kind == TrainingEntryKind.SelfReported ? selfReportedMinutes : null,
        };
        entry.Rolls.AddRange(BuildRolls(entry.Id, rolls));

        db.TrainingEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>Rewrites an own entry in place; the roll list is replaced wholesale.</summary>
    public async Task UpdateAsync(
        Guid requestingUserId,
        Guid entryId,
        TrainingEntryKind kind,
        DateOnly entryDate,
        Guid? sessionId,
        string? notes,
        int? selfReportedMinutes,
        IReadOnlyList<(Guid? PartnerPersonId, string PartnerLabel, string? Summary)> rolls,
        CancellationToken ct = default)
    {
        var entry = await GetEntryAsync(requestingUserId, entryId, ct)
            ?? throw new InvalidOperationException("Entry not found.");

        await ValidateAsync(kind, selfReportedMinutes, sessionId, ct);

        entry.Kind = kind;
        entry.EntryDate = entryDate;
        entry.SessionId = sessionId;
        entry.Notes = notes;
        entry.SelfReportedMinutes = kind == TrainingEntryKind.SelfReported ? selfReportedMinutes : null;

        db.TrainingRolls.RemoveRange(entry.Rolls);
        entry.Rolls.Clear();
        entry.Rolls.AddRange(BuildRolls(entry.Id, rolls));

        // Explicit Add: rolls carry client-set keys, so graph discovery would
        // classify them Modified and skip the tenant stamp.
        db.TrainingRolls.AddRange(entry.Rolls);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes an own entry and its rolls.</summary>
    public async Task DeleteAsync(Guid requestingUserId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await GetEntryAsync(requestingUserId, entryId, ct)
            ?? throw new InvalidOperationException("Entry not found.");

        db.TrainingEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The member-facing two-tier split: verified hours from Confirmed attendance,
    /// self-reported hours from the diary. Owner statistics use the verified tier only.
    /// </summary>
    public async Task<TwoTierHours> HoursAsync(Guid requestingUserId, CancellationToken ct = default)
    {
        if (await OwnPersonIdAsync(requestingUserId, ct) is not { } personId)
        {
            return new TwoTierHours(0, 0);
        }

        var verifiedMinutes = await db.AttendanceRecords
            .Where(a => a.PersonId == personId && a.Status == Domain.Attendance.AttendanceStatus.Confirmed)
            .SumAsync(a => (int?)a.Session.DurationMinutes, ct) ?? 0;

        var selfMinutes = await db.TrainingEntries
            .Where(e => e.PersonId == personId && e.SelfReportedMinutes != null)
            .SumAsync(e => (int?)e.SelfReportedMinutes, ct) ?? 0;

        return new TwoTierHours(verifiedMinutes / 60, selfMinutes / 60);
    }
}
