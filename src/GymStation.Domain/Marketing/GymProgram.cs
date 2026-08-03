using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Marketing;

/// <summary>
/// A Program — the Gym's public marketing offering (BJJ, Bootcamp, Muay Thai):
/// what a prospective member joins for, NOT how the schedule is tagged (that's
/// ClassType). Lists on the public page in SortOrder; archived ones vanish
/// there but keep their content. Class named GymProgram for the same reason
/// Event became GymEvent — the glossary term stays "Program".
/// </summary>
public class GymProgram : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Title { get; set; }

    /// <summary>Markdown; renders through the one audited pipeline (#133).</summary>
    public string? Description { get; set; }

    /// <summary>File-store path of the 1:1 program image; null renders a text card.</summary>
    public string? ImagePath { get; set; }

    public int SortOrder { get; set; }
    public bool Archived { get; set; }
}
