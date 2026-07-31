namespace GymStation.Infrastructure.Tenancy;

/// <summary>The Gym the current request operates as. Query filters and the write guard key off this.</summary>
public interface ITenantContext
{
    Guid? CurrentGymId { get; }
}

/// <summary>Scoped mutable holder; hydrated per request from the active-gym claim, or directly in tests.</summary>
public class TenantContext : ITenantContext
{
    public Guid? CurrentGymId { get; private set; }

    public void SetGym(Guid gymId) => CurrentGymId = gymId;

    public void Clear() => CurrentGymId = null;
}
