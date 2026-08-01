using GymStation.Domain.Money;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Tests;

public class BreakEvenMathTests
{
    [Fact]
    public void MatchesTheDesignMockupNumbers()
    {
        // The exact scenario from the Academy Ledger reports mockup.
        var result = BreakEvenMath.Calculate(new BreakEvenInput(
            ActiveMembers: 87,
            Arpm: 85m,
            MonthlyFixedCosts: 5800m,
            ProcessingRatePercent: 2.9m,
            StaffingPercentOfRevenue: 20m,
            TargetOwnerSalary: 3000m));

        Assert.Equal(65.54m, result.ContributionPerMember, 2);
        Assert.Equal(89, result.MembersToCoverExpenses);
        Assert.Equal(135, result.MembersForTargetSalary);
        Assert.Equal(7522.70m, result.MonthlyRevenueBreakEven, 0);
    }

    [Fact]
    public void RejectsImpossibleVariableCosts()
    {
        Assert.Throws<InvalidOperationException>(() => BreakEvenMath.Calculate(
            new BreakEvenInput(10, 85m, 1000m, 50m, 50m, 0m)));
    }

    [Fact]
    public void RejectsNonPositiveArpm()
    {
        Assert.Throws<InvalidOperationException>(() => BreakEvenMath.Calculate(
            new BreakEvenInput(10, 0m, 1000m, 2.9m, 20m, 0m)));
    }

    [Fact]
    public void ZeroSalary_MakesBothMemberTargetsEqual()
    {
        var result = BreakEvenMath.Calculate(new BreakEvenInput(10, 100m, 1000m, 0m, 0m, 0m));

        Assert.Equal(result.MembersToCoverExpenses, result.MembersForTargetSalary);
        Assert.Equal(10, result.MembersToCoverExpenses);
    }
}

public class ThemeMathTests
{
    [Fact]
    public void DefaultAccent_PassesBothModes()
    {
        Assert.True(ThemeMath.IsAccessibleAccent("#C9503B"));
    }

    [Theory]
    [InlineData("#171B21")] // the dark background itself — no contrast on dark
    [InlineData("#F5F2EA")] // near the paper background — no contrast on light
    [InlineData("not-a-color")]
    [InlineData("#FFF")]
    [InlineData(null)]
    public void RejectsInaccessibleOrMalformedAccents(string? hex)
    {
        Assert.False(ThemeMath.IsAccessibleAccent(hex));
    }

    [Fact]
    public void ContrastRatio_IsSymmetric_AndKnownValue()
    {
        // Black vs white is the canonical 21:1.
        Assert.Equal(21.0, ThemeMath.ContrastRatio("#000000", "#FFFFFF"), 1);
        Assert.Equal(
            ThemeMath.ContrastRatio("#C9503B", "#171B21"),
            ThemeMath.ContrastRatio("#171B21", "#C9503B"), 6);
    }
}
