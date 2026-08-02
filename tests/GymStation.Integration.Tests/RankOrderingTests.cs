using GymStation.Domain.Ranks;
using GymStation.Infrastructure.Ranks;

namespace GymStation.Integration.Tests;

/// <summary>Pure ordering tests — no database; mirrors Joshua's spec enumeration.</summary>
public class RankOrderingTests
{
    private static CurrentRank At(Guid systemId, int order, int stripes)
        => new(
            new Rank
            {
                Id = Guid.NewGuid(),
                RankSystemId = systemId,
                Name = $"r{order}",
                Order = order,
                BandColorHex = "#000000",
                BarColorHex = "#000000",
            },
            stripes,
            new DateOnly(2026, 1, 1));

    [Fact]
    public void FullSpecOrder_Holds()
    {
        var kids = IbjjfSeed.KidsSystemId;
        var adult = IbjjfSeed.AdultSystemId;

        // no belt < kids White < kids Grey-White … < adult White < White+1 … <
        // Black < Black+6 < Red & Black (order 6) < Red & White (7) < Red (8).
        CurrentRank?[] spec =
        [
            null,
            At(kids, 0, 0),  // kids White
            At(kids, 1, 0),  // Grey-White
            At(kids, 12, 4), // Green-Black, max stripes
            At(adult, 1, 0), // adult White
            At(adult, 1, 1), // White + 1 stripe
            At(adult, 1, 4),
            At(adult, 2, 0), // Blue
            At(adult, 4, 4), // Brown + 4
            At(adult, 5, 0), // Black
            At(adult, 5, 6), // Black 6th degree
            At(adult, 6, 0), // Red & Black (7th)
            At(adult, 7, 0), // Red & White (8th)
            At(adult, 8, 0), // Red (9th)
        ];

        var keys = spec.Select(RankOrdering.Key).ToList();
        Assert.Equal(keys.OrderBy(k => k).ToList(), keys); // already ascending
        Assert.Equal(keys.Count, keys.Distinct().Count()); // strictly increasing
    }

    [Fact]
    public void CustomSystems_SortAboveTheSeededLadders()
    {
        var custom = At(Guid.NewGuid(), 1, 0);
        Assert.True(RankOrdering.Key(custom) > RankOrdering.Key(At(IbjjfSeed.AdultSystemId, 8, 0)));
    }
}
