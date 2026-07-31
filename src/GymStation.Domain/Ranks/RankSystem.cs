namespace GymStation.Domain.Ranks;

/// <summary>
/// An ordered ladder of Ranks for one discipline. Platform-seeded systems (IBJJF adult,
/// IBJJF kids) have a null GymId and are visible to every gym; custom systems belong to
/// one gym. A Person holds at most one active rank per system (derived from RankAwards).
/// </summary>
public class RankSystem
{
    public Guid Id { get; set; }

    /// <summary>Null = platform-seeded, visible to all gyms. Otherwise a per-gym custom ladder.</summary>
    public Guid? GymId { get; set; }

    public required string Name { get; set; }

    public bool IsSeeded { get; set; }

    public List<Rank> Ranks { get; set; } = [];
}
