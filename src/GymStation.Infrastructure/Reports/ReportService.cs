using GymStation.Domain.Attendance;
using GymStation.Domain.Money;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Reports;

public record MonthMoney(DateOnly Month, decimal Collected, decimal Expenses);

public class ReportService(GymStationDbContext db)
{
    /// <summary>Collected payments vs logged expenses per month, oldest first.</summary>
    public async Task<List<MonthMoney>> MoneySeriesAsync(DateOnly today, int months = 6, CancellationToken ct = default)
    {
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var payments = await db.Payments.AsNoTracking().Where(p => p.ReceivedOn >= firstMonth).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(x => x.SpentOn >= firstMonth).ToListAsync(ct);

        var series = new List<MonthMoney>(months);
        for (var i = 0; i < months; i++)
        {
            var month = firstMonth.AddMonths(i);
            var next = month.AddMonths(1);
            series.Add(new MonthMoney(
                month,
                payments.Where(p => p.ReceivedOn >= month && p.ReceivedOn < next).Sum(p => p.Amount),
                expenses.Where(x => x.SpentOn >= month && x.SpentOn < next).Sum(x => x.Amount)));
        }

        return series;
    }

    /// <summary>Gym-wide confirmed check-ins per week for the trend bars, oldest first.</summary>
    public async Task<List<int>> WeeklyCheckinsAsync(DateOnly today, int weeks = 12, CancellationToken ct = default)
    {
        var from = today.AddDays(-7 * weeks);
        var confirmed = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Status == AttendanceStatus.Confirmed && a.Session.Date >= from)
            .Select(a => a.Session.Date)
            .ToListAsync(ct);

        var weekly = new int[weeks];
        foreach (var date in confirmed)
        {
            var age = today.DayNumber - date.DayNumber;
            weekly[weeks - 1 - Math.Min(age / 7, weeks - 1)]++;
        }

        return [.. weekly];
    }

    /// <summary>
    /// Live-data prefill for the break-even calculator. Every value remains owner-editable
    /// on the form — these are starting points, not assumptions.
    /// </summary>
    public async Task<BreakEvenInput> BreakEvenPrefillAsync(DateOnly today, CancellationToken ct = default)
    {
        var activeMembers = await db.Persons.CountAsync(p => !p.Archived && p.MembershipPlanId != null, ct);

        var plannedPrices = await db.Persons
            .Where(p => !p.Archived && p.MembershipPlanId != null)
            .Join(db.MembershipPlans, p => p.MembershipPlanId, pl => pl.Id, (p, pl) => pl.Price)
            .ToListAsync(ct);
        var arpm = plannedPrices.Count > 0 ? decimal.Round(plannedPrices.Average(), 2) : 0m;

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonthExpenses = await db.Expenses
            .Where(x => x.SpentOn >= lastMonthStart && x.SpentOn < monthStart)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        return new BreakEvenInput(
            ActiveMembers: activeMembers,
            Arpm: arpm,
            MonthlyFixedCosts: lastMonthExpenses,
            ProcessingRatePercent: 2.9m,
            StaffingPercentOfRevenue: 20m,
            TargetOwnerSalary: 0m);
    }
}
