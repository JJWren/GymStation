using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

/// <summary>
/// A filterable tag on templates/sessions (gi, no-gi, kids, open-mat, custom).
/// Tag colors are per-gym data — the tenant owns this taxonomy.
/// </summary>
public class ClassType : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Name { get; set; }

    public string ColorHex { get; set; } = "#707886";

    public bool Archived { get; set; }
}
