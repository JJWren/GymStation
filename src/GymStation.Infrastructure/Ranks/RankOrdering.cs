using GymStation.Domain.Ranks;

namespace GymStation.Infrastructure.Ranks;

/// <summary>
/// One total order over everyone's rank, per Joshua's spec (2026-08-01):
/// no belt &lt; kids ladder &lt; adult ladder &lt; custom systems; within a ladder by belt
/// Order; within a belt each stripe/degree is one step higher (White &lt; White+1 …).
/// The 7th–9th degree red belts are their own Ranks, so belt Order carries them.
/// </summary>
public static class RankOrdering
{
    /// <summary>Ascending key: lowest rank first; no belt sorts below everything.</summary>
    public static long Key(CurrentRank? current)
    {
        if (current is null)
        {
            return long.MinValue;
        }

        var system = current.Rank.RankSystemId == IbjjfSeed.KidsSystemId ? 0
            : current.Rank.RankSystemId == IbjjfSeed.AdultSystemId ? 1
            : 2;

        return ((long)system << 32) | ((long)(uint)current.Rank.Order << 8) | (uint)current.Stripes;
    }
}
