namespace GymStation.Domain.Money;

/// <summary>One lever of the pricing what-if (#221): a plan (or plan-shaped
/// revenue line) at a price with a member count. Revenue = price × members.</summary>
public record PlanWhatIfRow(string Name, decimal Price, int Members)
{
    public decimal Revenue => Price * Members;
}

public record PricingWhatIfResult(
    decimal MonthlyRevenue,
    decimal Contribution,
    decimal NetAfterFixedCosts,
    decimal NetAfterTargetSalary,
    decimal MonthlyRevenueBreakEven);

/// <summary>
/// The pricing what-if calculator (#221): "what if I raise dues $10, or move 20
/// people onto the family plan?" It shares the break-even model's cost shape —
/// variable costs as a fraction of revenue, fixed costs, target owner salary —
/// so its break-even line agrees with BreakEvenMath to the cent. Owner-editable
/// end to end (the Q3/Q4 principle): nothing is hardcoded to a gym's costs.
/// </summary>
public static class PricingWhatIfMath
{
    public static PricingWhatIfResult Calculate(
        IReadOnlyCollection<PlanWhatIfRow> rows,
        decimal monthlyFixedCosts,
        decimal processingRatePercent,
        decimal staffingPercentOfRevenue,
        decimal targetOwnerSalary)
    {
        // Every input is owner-typed — refuse nonsense loudly instead of
        // projecting negative revenue or contribution above revenue.
        if (rows.Any(r => r.Price < 0 || r.Members < 0))
        {
            throw new InvalidOperationException("Prices and member counts can't be negative.");
        }

        if (processingRatePercent < 0 || staffingPercentOfRevenue < 0 || monthlyFixedCosts < 0 || targetOwnerSalary < 0)
        {
            throw new InvalidOperationException("Percentages, fixed costs, and target salary can't be negative.");
        }

        var variableFraction = (processingRatePercent + staffingPercentOfRevenue) / 100m;
        if (variableFraction >= 1m)
        {
            throw new InvalidOperationException("Variable costs consume 100% of revenue — break-even is unreachable.");
        }

        var revenue = rows.Sum(r => r.Revenue);
        var contribution = revenue * (1m - variableFraction);

        return new PricingWhatIfResult(
            MonthlyRevenue: decimal.Round(revenue, 2),
            Contribution: decimal.Round(contribution, 2),
            NetAfterFixedCosts: decimal.Round(contribution - monthlyFixedCosts, 2),
            NetAfterTargetSalary: decimal.Round(contribution - monthlyFixedCosts - targetOwnerSalary, 2),
            // The same formula BreakEvenMath uses — the two calculators must agree.
            MonthlyRevenueBreakEven: decimal.Round(monthlyFixedCosts / (1m - variableFraction), 2));
    }
}
