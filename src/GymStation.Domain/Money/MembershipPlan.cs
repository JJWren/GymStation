using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Money;

public enum PlanCadence
{
    /// <summary>Raises a Charge on the 1st of each month for every Person on the plan.</summary>
    Monthly = 0,

    /// <summary>No automatic cycle — charges are raised ad hoc (drop-ins).</summary>
    PerVisit = 1,
}

/// <summary>Who a plan covers: one Person, or a whole Family in one charge (#91).</summary>
public enum PlanScope
{
    PerPerson = 0,
    Family = 1,
}

/// <summary>A gym's price + billing cadence. GymStation tracks money; it never moves it.</summary>
public class MembershipPlan : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Name { get; set; }
    public decimal Price { get; set; }
    public PlanCadence Cadence { get; set; } = PlanCadence.Monthly;

    /// <summary>Family plans bill the family's PRIMARY guardian's own Person once per
    /// cycle and the individual cycle skips every covered member (#91).</summary>
    public PlanScope Scope { get; set; } = PlanScope.PerPerson;

    /// <summary>
    /// Family-size pricing (#181, ADR 0005) — family-scope plans only. Price covers
    /// IncludedAdults non-ward members and IncludedKids wards; each member beyond a
    /// lane adds its extra price, per head, per cycle (strict lanes — adult and kid
    /// slots never pool). All zeros = flat rate. Owners bake discounts into these
    /// numbers; there is no percent-off knob. "Comped" means the COMPUTED total is
    /// zero, not the base.
    /// </summary>
    public int IncludedAdults { get; set; }

    /// <inheritdoc cref="IncludedAdults" />
    public int IncludedKids { get; set; }

    /// <inheritdoc cref="IncludedAdults" />
    public decimal ExtraAdultPrice { get; set; }

    /// <inheritdoc cref="IncludedAdults" />
    public decimal ExtraKidPrice { get; set; }

    public bool Archived { get; set; }
}
