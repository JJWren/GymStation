using GymStation.Domain.Tenancy;

namespace GymStation.Domain.People;

/// <summary>
/// A gym's roster record for a human. May exist with no User at all
/// (children, members without devices); guardians manage those via GuardianLink.
/// </summary>
public class Person : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    /// <summary>Global login this record belongs to; null for people without accounts.</summary>
    public Guid? UserId { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string DisplayName => $"{FirstName} {LastName}";

    public PersonRoles Roles { get; set; } = PersonRoles.Member;

    /// <summary>Needed for IBJJF age groups (derived from birth year, never stored) and kids ladders.</summary>
    public DateOnly? DateOfBirth { get; set; }

    public DateOnly JoinedOn { get; set; }

    /// <summary>Stored path in the file store; null renders the silhouette placeholder.</summary>
    public string? PortraitPath { get; set; }

    /// <summary>The MembershipPlan this Person is on; null = no automatic charges.</summary>
    public Guid? MembershipPlanId { get; set; }

    public bool Archived { get; set; }

    public bool HasRole(PersonRoles role) => role != PersonRoles.None && (Roles & role) == role;
}
