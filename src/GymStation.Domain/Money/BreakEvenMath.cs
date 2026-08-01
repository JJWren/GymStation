namespace GymStation.Domain.Money;

public record BreakEvenInput(
    int ActiveMembers,
    decimal Arpm,
    decimal MonthlyFixedCosts,
    decimal ProcessingRatePercent,
    decimal StaffingPercentOfRevenue,
    decimal TargetOwnerSalary);

public record BreakEvenResult(
    decimal ContributionPerMember,
    int MembersToCoverExpenses,
    int MembersForTargetSalary,
    decimal MonthlyRevenueBreakEven);

/// <summary>
/// The break-even calculator from the original brief. Every input is owner-editable —
/// nothing here is ever hardcoded to a gym's assumed cost structure (the Q3/Q4 principle).
/// </summary>
public static class BreakEvenMath
{
    public static BreakEvenResult Calculate(BreakEvenInput input)
    {
        var variableFraction = (input.ProcessingRatePercent + input.StaffingPercentOfRevenue) / 100m;
        if (variableFraction >= 1m)
        {
            throw new InvalidOperationException("Variable costs consume 100% of revenue — break-even is unreachable.");
        }

        if (input.Arpm <= 0)
        {
            throw new InvalidOperationException("ARPM must be positive.");
        }

        var contribution = input.Arpm * (1m - variableFraction);

        return new BreakEvenResult(
            ContributionPerMember: decimal.Round(contribution, 2),
            MembersToCoverExpenses: (int)Math.Ceiling(input.MonthlyFixedCosts / contribution),
            MembersForTargetSalary: (int)Math.Ceiling((input.MonthlyFixedCosts + input.TargetOwnerSalary) / contribution),
            MonthlyRevenueBreakEven: decimal.Round(input.MonthlyFixedCosts / (1m - variableFraction), 2));
    }
}
