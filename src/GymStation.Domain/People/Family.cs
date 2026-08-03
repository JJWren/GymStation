using GymStation.Domain.Tenancy;

namespace GymStation.Domain.People;

/// <summary>
/// The billing/guardianship aggregate (#89): member Persons (wards or adults) plus
/// guardian LOGINS with per-guardian permission flags. Absorbs the old GuardianLink.
/// One family per Person per gym; guardians are Users, so a guardian needs no roster
/// Person unless they also train.
/// </summary>
public class Family : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    /// <summary>Display name — "HALE FAMILY" by default, editable.</summary>
    public required string Name { get; set; }

    /// <summary>Family-scope plan (#91): when set, the cycle bills the primary
    /// guardian's own Person once and skips covered members.</summary>
    public Guid? MembershipPlanId { get; set; }

    public List<FamilyMember> Members { get; set; } = [];
    public List<FamilyGuardian> Guardians { get; set; } = [];
}

/// <summary>A roster Person inside a Family. IsWard = guardians act for them.</summary>
public class FamilyMember : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid FamilyId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    /// <summary>Wards are managed by the family's guardians (check-in, RSVP, diary —
    /// the guardians ARE the account authority). Adults in a family are billing-only.</summary>
    public bool IsWard { get; set; }
}

/// <summary>
/// A guardian LOGIN on a Family with its permission flags. Exactly one guardian per
/// family is PRIMARY: holds every flag (locked), receives the family bill (#91), and
/// can transfer primacy. Flags gate what non-primary guardians may do.
/// </summary>
public class FamilyGuardian : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid FamilyId { get; set; }

    /// <summary>The guardian's global User id (logins are global; families are per-gym).</summary>
    public Guid GuardianUserId { get; set; }

    public bool IsPrimary { get; set; }

    /// <summary>Act for wards: check-in, RSVP, diary, progress. Default ON — it's
    /// what being a guardian is for.</summary>
    public bool ActForWards { get; set; } = true;

    /// <summary>Invite/remove other guardians and edit their flags (never the primary's).</summary>
    public bool ManageGuardians { get; set; }

    /// <summary>Add/remove family members and toggle ward status.</summary>
    public bool ManageMembers { get; set; }

    /// <summary>See the family's billing card (plan, charges, arrears).</summary>
    public bool ViewBilling { get; set; }
}
