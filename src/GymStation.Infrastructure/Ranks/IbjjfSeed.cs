using GymStation.Domain.Ranks;

namespace GymStation.Infrastructure.Ranks;

/// <summary>
/// Platform-level IBJJF ladders (GymId = null), seeded via migration data with stable ids.
/// Band/bar colors are the design system's sacred belt colors — identical for every tenant.
/// </summary>
public static class IbjjfSeed
{
    public static readonly Guid AdultSystemId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid KidsSystemId = Guid.Parse("11111111-1111-1111-1111-111111111102");

    private const string Black = "#17181A";
    private const string RedBar = "#A31D26";

    public static RankSystem[] Systems() =>
    [
        new() { Id = AdultSystemId, GymId = null, Name = "IBJJF Adult", IsSeeded = true },
        new() { Id = KidsSystemId, GymId = null, Name = "IBJJF Kids", IsSeeded = true },
    ];

    public static Rank[] Ranks()
    {
        Rank Adult(string idSuffix, string name, int order, string band, string bar, int maxStripes) => new()
        {
            Id = Guid.Parse($"11111111-1111-1111-1111-1111111112{idSuffix}"),
            RankSystemId = AdultSystemId,
            Name = name,
            Order = order,
            BandColorHex = band,
            BarColorHex = bar,
            MaxStripes = maxStripes,
        };

        Rank Kids(string idSuffix, string name, int order, string band) => new()
        {
            Id = Guid.Parse($"11111111-1111-1111-1111-1111111113{idSuffix}"),
            RankSystemId = KidsSystemId,
            Name = name,
            Order = order,
            BandColorHex = band,
            BarColorHex = Black,
            MaxStripes = 4,
        };

        return
        [
            Adult("01", "White", 1, "#E9E6DC", Black, 4),
            Adult("02", "Blue", 2, "#2456A6", Black, 4),
            Adult("03", "Purple", 3, "#5C3D93", Black, 4),
            Adult("04", "Brown", 4, "#7A5230", Black, 4),
            Adult("05", "Black", 5, Black, RedBar, 6),

            // Degrees past black are their own belts (no stripes): 7th, 8th, 9th.
            Adult("06", "Red & Black", 6, "#521A1E", Black, 0),
            Adult("07", "Red & White", 7, RedBar, "#E9E6DC", 0),
            Adult("08", "Red", 8, RedBar, "#7A1218", 0),

            // Order 0 slots kids White ahead of the greys without renumbering.
            Kids("00", "White", 0, "#E9E6DC"),
            Kids("01", "Grey-White", 1, "#B8BDC6"),
            Kids("02", "Grey", 2, "#9BA1AB"),
            Kids("03", "Grey-Black", 3, "#7E848E"),
            Kids("04", "Yellow-White", 4, "#F0D275"),
            Kids("05", "Yellow", 5, "#E8C13A"),
            Kids("06", "Yellow-Black", 6, "#C7A32E"),
            Kids("07", "Orange-White", 7, "#EBA06A"),
            Kids("08", "Orange", 8, "#E07B39"),
            Kids("09", "Orange-Black", 9, "#BF6830"),
            Kids("10", "Green-White", 10, "#74B18C"),
            Kids("11", "Green", 11, "#3E8E5A"),
            Kids("12", "Green-Black", 12, "#33774B"),
        ];
    }
}
