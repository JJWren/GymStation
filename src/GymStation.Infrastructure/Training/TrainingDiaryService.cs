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

        if (kind == TrainingEntryKind.SelfReported && selfReportedMinutes is not > 0)
        {
            throw new InvalidOperationException("Self-reported entries need a positive number of minutes.");
        }

        if (sessionId is { } sid && !await db.ClassSessions.AnyAsync(s => s.Id == sid, ct))
        {
            throw new InvalidOperationException("Linked session not found in the active gym.");
        }

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

        foreach (var (partnerPersonId, partnerLabel, summary) in rolls.Where(r => !string.IsNullOrWhiteSpace(r.PartnerLabel)))
        {
            entry.Rolls.Add(new TrainingRoll
            {
                Id = Guid.NewGuid(),
                TrainingEntryId = entry.Id,
                PartnerPersonId = partnerPersonId,
                PartnerLabel = partnerLabel.Trim(),
                Summary = summary,
            });
        }

        db.TrainingEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
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
