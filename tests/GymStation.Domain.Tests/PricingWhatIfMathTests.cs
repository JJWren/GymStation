using GymStation.Domain.Money;

namespace GymStation.Domain.Tests;

public class PricingWhatIfMathTests
{
    [Fact]
    public void ProjectsRevenueAndNets()
    {
        var result = PricingWhatIfMath.Calculate(
            [
                new PlanWhatIfRow("Adult Unlimited", 95m, 100),   // the "$10 raise" scenario
                new PlanWhatIfRow("Kids BJJ", 65m, 40),
                new PlanWhatIfRow("Family Standard (4 families, live total)", 700m, 1),
            ],
            monthlyFixedCosts: 7000m,
            processingRatePercent: 2.9m,
            staffingPercentOfRevenue: 20m,
            targetOwnerSalary: 3000m);

        Assert.Equal(12800m, result.MonthlyRevenue); // 9500 + 2600 + 700
        Assert.Equal(decimal.Round(12800m * 0.771m, 2), result.Contribution);
        Assert.Equal(result.Contribution - 7000m, result.NetAfterFixedCosts);
        Assert.Equal(result.Contribution - 10000m, result.NetAfterTargetSalary);
    }

    [Fact]
    public void BreakEvenLine_AgreesWithBreakEvenMath()
    {
        // Same cost shape in both calculators must yield the same revenue
        // break-even — the what-if composes the break-even model, not a rival.
        var whatIf = PricingWhatIfMath.Calculate(
            [new PlanWhatIfRow("Any", 85m, 50)], 5200m, 2.9m, 20m, 0m);
        var breakEven = BreakEvenMath.Calculate(new BreakEvenInput(50, 85m, 5200m, 2.9m, 20m, 0m));

        Assert.Equal(breakEven.MonthlyRevenueBreakEven, whatIf.MonthlyRevenueBreakEven);
    }

    [Fact]
    public void EmptyLeversProjectZero_AndImpossibleCostsRefuse()
    {
        var empty = PricingWhatIfMath.Calculate([], 1000m, 2.9m, 20m, 0m);
        Assert.Equal(0m, empty.MonthlyRevenue);
        Assert.True(empty.NetAfterFixedCosts < 0);

        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", 10m, 1)], 100m, 60m, 40m, 0m));
    }

    [Fact]
    public void NegativeInputs_RefuseLoudly()
    {
        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", -10m, 1)], 100m, 2.9m, 20m, 0m));
        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", 10m, -1)], 100m, 2.9m, 20m, 0m));
        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", 10m, 1)], -100m, 2.9m, 20m, 0m));
        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", 10m, 1)], 100m, -2.9m, 20m, 0m));
        Assert.Throws<InvalidOperationException>(
            () => PricingWhatIfMath.Calculate([new PlanWhatIfRow("X", 10m, 1)], 100m, 2.9m, 20m, -50m));
    }
}
