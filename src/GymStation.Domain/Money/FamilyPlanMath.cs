namespace GymStation.Domain.Money;

/// <summary>
/// The family-size pricing formula (#181, ADR 0005). Strict per-category lanes:
/// unused adult slots never absorb kids, and vice versa. One field set expresses
/// a flat rate (all zeros), a standard size with per-extra increases, or pure
/// per-head pricing (zero base). Kid = ward — billing follows the modeled state,
/// never age.
/// </summary>
public static class FamilyPlanMath
{
    /// <summary>Computes a family's cycle charge for <paramref name="plan"/> given
    /// its counted non-ward members and wards. A zero Total is a comped family —
    /// the cycle raises nothing.</summary>
    public static (decimal Total, decimal ExtraAmount) Compute(MembershipPlan plan, int adults, int kids)
    {
        var extra = Math.Max(0, adults - plan.IncludedAdults) * plan.ExtraAdultPrice
                  + Math.Max(0, kids - plan.IncludedKids) * plan.ExtraKidPrice;
        return (plan.Price + extra, extra);
    }
}
