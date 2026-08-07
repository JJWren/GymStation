namespace GymStation.Domain.Ranks;

/// <summary>One rung of a RankSystem's ladder. Belt colors are sacred — never tenant-themed.</summary>
public class Rank
{
    public Guid Id { get; set; }
    public Guid RankSystemId { get; set; }

    public required string Name { get; set; }

    /// <summary>Position on the ladder, ascending.</summary>
    public int Order { get; set; }

    /// <summary>Stripes (or degrees, on black) a Person can hold at this rank.</summary>
    public int MaxStripes { get; set; } = 4;

    /// <summary>Band color of the physical belt strip.</summary>
    public required string BandColorHex { get; set; }

    /// <summary>Rank-bar color: black on colored belts, red on black belts.</summary>
    public required string BarColorHex { get; set; }

    /// <summary>Retired ranks (#220) take no NEW awards and leave pickers, but
    /// keep rendering everywhere history shows them. The delete path is only
    /// for unheld ranks — held ones retire instead.</summary>
    public bool Retired { get; set; }
}
