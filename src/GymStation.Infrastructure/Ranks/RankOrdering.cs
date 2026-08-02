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

        // 20-bit fields: clamps sit far beyond any real ladder (IBJJF max is 6 stripes)
        // while keeping the top field clear of long's sign bit.
        return ((long)system << 40)
            | ((long)Math.Clamp(current.Rank.Order, 0, 0xF_FFFF) << 20)
            | (uint)Math.Clamp(current.Stripes, 0, 0xF_FFFF);
    }
}
