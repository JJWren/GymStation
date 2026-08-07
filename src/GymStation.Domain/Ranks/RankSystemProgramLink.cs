using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Ranks;

/// <summary>
/// A Gym's mapping of one RankSystem to one of its Programs — the per-gym
/// discipline label for a ladder (ADR 0006). Lives beside the ladder rather
/// than on it because platform-seeded systems (GymId null) are shared by every
/// gym, while Programs are tenant-owned: each gym labels the shared IBJJF
/// ladders with ITS "BJJ" program. At most one link per (Gym, RankSystem);
/// unlinked ladders display their own name.
/// </summary>
public class RankSystemProgramLink : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }
    public Guid RankSystemId { get; set; }
    public Guid GymProgramId { get; set; }
}
