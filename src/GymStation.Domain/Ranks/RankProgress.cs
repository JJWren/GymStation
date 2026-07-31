namespace GymStation.Domain.Ranks;

public record CurrentRank(Rank Rank, int Stripes, DateOnly AtRankSince);

/// <summary>Derivations over a Person's award history within ONE RankSystem.</summary>
public static class RankProgress
{
    /// <summary>
    /// Current rank = the latest award; "at rank since" = the first award of the
    /// trailing run at that same rank (stripes don't reset the clock, belts do).
    /// </summary>
    public static CurrentRank? Current(IEnumerable<RankAward> awards)
    {
        var ordered = awards
            .OrderBy(a => a.AwardedOn)
            .ThenBy(a => a.RecordedUtc)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var latest = ordered[^1];
        var since = latest.AwardedOn;

        for (var i = ordered.Count - 1; i >= 0 && ordered[i].RankId == latest.RankId; i--)
        {
            since = ordered[i].AwardedOn;
        }

        return new CurrentRank(latest.Rank, latest.Stripes, since);
    }

    public static TimeSpan? TimeAtRank(IEnumerable<RankAward> awards, DateOnly today)
    {
        var current = Current(awards);
        return current is null
            ? null
            : today.ToDateTime(TimeOnly.MinValue) - current.AtRankSince.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Human-form duration like "2y 3m" (months are approximate by design).</summary>
    public static string FormatDuration(TimeSpan span)
    {
        var totalMonths = (int)(span.TotalDays / 30.44);
        var years = totalMonths / 12;
        var months = totalMonths % 12;

        return years switch
        {
            0 when months == 0 => "new",
            0 => $"{months}m",
            _ when months == 0 => $"{years}y",
            _ => $"{years}y {months}m",
        };
    }
}
