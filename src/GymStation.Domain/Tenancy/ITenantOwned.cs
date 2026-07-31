namespace GymStation.Domain.Tenancy;

/// <summary>
/// Marks an entity as belonging to exactly one Gym. Query filters hide other tenants' rows
/// and the write guard rejects cross-tenant mutations (see ADR 0001).
/// </summary>
public interface ITenantOwned
{
    Guid GymId { get; set; }
}
