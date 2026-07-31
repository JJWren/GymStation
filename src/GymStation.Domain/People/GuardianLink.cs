using GymStation.Domain.Tenancy;

namespace GymStation.Domain.People;

/// <summary>A User (guardian) managing a child Person that has no login of its own.</summary>
public class GuardianLink : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    /// <summary>The guardian's global User id.</summary>
    public Guid GuardianUserId { get; set; }

    public Guid ChildPersonId { get; set; }
    public Person ChildPerson { get; set; } = null!;
}
