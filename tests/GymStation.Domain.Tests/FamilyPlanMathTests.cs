using GymStation.Domain.Money;

namespace GymStation.Domain.Tests;

/// <summary>The #181 pricing formula (ADR 0005): strict lanes, clamped extras,
/// comped = computed total of zero.</summary>
public class FamilyPlanMathTests
{
    private static MembershipPlan Plan(decimal price, int inclAdults, int inclKids, decimal extraAdult, decimal extraKid) => new()
    {
        Name = "Family",
        Price = price,
        Scope = PlanScope.Family,
        IncludedAdults = inclAdults,
        IncludedKids = inclKids,
        ExtraAdultPrice = extraAdult,
        ExtraKidPrice = extraKid,
    };

    [Fact]
    public void FlatPlan_IgnoresSize()
        => Assert.Equal((150m, 0m), FamilyPlanMath.Compute(Plan(150, 0, 0, 0, 0), 4, 6));

    [Fact]
    public void StandardSizePlusExtras_ChargesPerLane()
        // 2+2 included; 3 adults & 4 kids → 1 extra adult ($30) + 2 extra kids ($40).
        => Assert.Equal((220m, 70m), FamilyPlanMath.Compute(Plan(150, 2, 2, 30, 20), 3, 4));

    [Fact]
    public void LanesNeverPool()
        // 1 adult + 3 kids against 2+2: the unused adult slot does NOT absorb the third kid.
        => Assert.Equal((170m, 20m), FamilyPlanMath.Compute(Plan(150, 2, 2, 30, 20), 1, 3));

    [Fact]
    public void UnderIncludedCounts_ClampAtBase()
        => Assert.Equal((150m, 0m), FamilyPlanMath.Compute(Plan(150, 2, 2, 30, 20), 0, 0));

    [Fact]
    public void PurePerHead_ZeroBaseStillPrices()
        => Assert.Equal((180m, 180m), FamilyPlanMath.Compute(Plan(0, 0, 0, 80, 50), 1, 2));

    [Fact]
    public void ComputedZero_IsComped()
        => Assert.Equal((0m, 0m), FamilyPlanMath.Compute(Plan(0, 0, 0, 0, 0), 3, 2));
}
