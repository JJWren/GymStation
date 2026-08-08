using GymStation.Domain.Attendance;
using GymStation.Domain.Money;
using GymStation.Domain.Ranks;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Reports;

public record MonthMoney(DateOnly Month, decimal Collected, decimal OtherIncome, decimal Expenses)
{
    public decimal TotalIn => Collected + OtherIncome;
}

/// <summary>One schedule slot's turnout over the window (#221).</summary>
public record SlotUtilization(string Name, int Sessions, int CheckIns)
{
    public decimal AvgPerSession => Sessions == 0 ? 0 : decimal.Round((decimal)CheckIns / Sessions, 1);
}

public record TypeUtilization(string TypeName, string ColorHex, int CheckIns);

public record InstructorLoad(string Name, int Sessions, int CheckIns);

/// <summary>One person's standing in one discipline (#221): how long at the
/// current rank, for the promotion pipeline.</summary>
public record PipelineRow(Guid PersonId, string PersonName, string Discipline, string RankName, int Stripes, DateOnly AtRankSince)
{
    public int MonthsAtRank(DateOnly today) => Math.Max(0, ((today.Year - AtRankSince.Year) * 12) + today.Month - AtRankSince.Month - (today.Day < AtRankSince.Day ? 1 : 0));
}

/// <summary>One member's outstanding dues (#221), aged from the oldest cycle
/// their payments no longer cover.</summary>
public record AgingRow(Guid PersonId, string PersonName, decimal Balance, DateOnly OldestUnpaidOn)
{
    public int DaysOverdue(DateOnly today) => Math.Max(0, today.DayNumber - OldestUnpaidOn.DayNumber);
}

public class ReportService(GymStationDbContext db)
{
    /// <summary>Collected payments vs logged expenses per month, oldest first.</summary>
    public async Task<List<MonthMoney>> MoneySeriesAsync(DateOnly today, int months = 6, CancellationToken ct = default)
    {
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.VoidedUtc == null && p.ReceivedOn >= firstMonth)
            .ToListAsync(ct);
        var otherIncome = await db.OtherIncomes.AsNoTracking().Where(x => x.ReceivedOn >= firstMonth).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(x => x.SpentOn >= firstMonth).ToListAsync(ct);

        var series = new List<MonthMoney>(months);
        for (var i = 0; i < months; i++)
        {
            var month = firstMonth.AddMonths(i);
            var next = month.AddMonths(1);
            series.Add(new MonthMoney(
                month,
                payments.Where(p => p.ReceivedOn >= month && p.ReceivedOn < next).Sum(p => p.Amount),
                otherIncome.Where(x => x.ReceivedOn >= month && x.ReceivedOn < next).Sum(x => x.Amount),
                expenses.Where(x => x.SpentOn >= month && x.SpentOn < next).Sum(x => x.Amount)));
        }

        return series;
    }

    /// <summary>Gym-wide confirmed check-ins per Sunday-start calendar week, oldest first.</summary>
    public async Task<List<WeekCount>> WeeklyCheckinsAsync(DateOnly today, int weeks = 12, CancellationToken ct = default)
    {
        var starts = StatWeeks.Starts(today, weeks);
        var from = starts[0];
        var dates = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Status == AttendanceStatus.Confirmed && a.Session.Date >= from && a.Session.Date <= today)
            .Select(a => a.Session.Date)
            .ToListAsync(ct);

        var byWeek = dates.GroupBy(StatWeeks.SundayOf).ToDictionary(g => g.Key, g => g.Count());
        return [.. starts.Select(s => new WeekCount(s, byWeek.GetValueOrDefault(s)))];
    }

    /// <summary>Turnout per schedule slot, per class type, and per instructor over
    /// the trailing window (#221) — confirmed attendance only, like every stat.</summary>
    public async Task<(List<SlotUtilization> Slots, List<TypeUtilization> Types, List<InstructorLoad> Instructors)> UtilizationAsync(
        DateOnly today, int weeks = 12, CancellationToken ct = default)
    {
        var from = today.AddDays(-7 * weeks);
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= today && s.Status != Domain.Scheduling.SessionStatus.Cancelled)
            .Include(s => s.ClassTypes)
            .ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();

        // Filter AND group at the database: only the window's non-cancelled
        // sessions ever leave Postgres, as (SessionId, Count) pairs.
        var bySession = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Status == AttendanceStatus.Confirmed && sessionIds.Contains(a.SessionId))
            .GroupBy(a => a.SessionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Group by TEMPLATE, not name — two "Fundamentals" slots at different
        // times are different classes. One-offs pool per name; template slots
        // label with their latest session's day/time (sessions can move).
        var slots = sessions
            .GroupBy(s => s.TemplateId?.ToString() ?? $"one-off:{s.Name}")
            .Select(g =>
            {
                var latest = g.OrderByDescending(s => s.Date).ThenByDescending(s => s.StartTime).First();
                var label = latest.TemplateId is null
                    ? $"{latest.Name} (one-offs)"
                    : $"{latest.Name} · {latest.Date.DayOfWeek.ToString()[..3].ToUpperInvariant()} {latest.StartTime:HH\\:mm}";
                return new SlotUtilization(label, g.Count(), g.Sum(s => bySession.GetValueOrDefault(s.Id)));
            })
            .OrderByDescending(s => s.CheckIns)
            .ToList();

        var types = sessions
            .SelectMany(s => s.ClassTypes.Select(t => (Type: t, Count: bySession.GetValueOrDefault(s.Id))))
            .GroupBy(x => (x.Type.Name, x.Type.ColorHex))
            .Select(g => new TypeUtilization(g.Key.Name, g.Key.ColorHex, g.Sum(x => x.Count)))
            .OrderByDescending(t => t.CheckIns)
            .ToList();

        var instructorIds = sessions.Where(s => s.InstructorPersonId != null).Select(s => s.InstructorPersonId!.Value).Distinct().ToList();
        var names = await db.Persons.AsNoTracking().IgnoreQueryFilters()
            .Where(p => instructorIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var instructors = sessions
            .Where(s => s.InstructorPersonId is not null)
            .GroupBy(s => s.InstructorPersonId!.Value)
            .Select(g => new InstructorLoad(names.GetValueOrDefault(g.Key, "—"), g.Count(), g.Sum(s => bySession.GetValueOrDefault(s.Id))))
            .OrderByDescending(x => x.Sessions)
            .ToList();

        return (slots, types, instructors);
    }

    /// <summary>Time-at-rank per person per discipline (#221), longest first —
    /// the promotion pipeline. Current rank derives from live awards, so #220
    /// deletions and #215 primaries need no special handling here.</summary>
    public async Task<List<PipelineRow>> PromotionPipelineAsync(CancellationToken ct = default)
    {
        // RankProgress.Current consumes RankAward entities (dates, stripes, the
        // Rank itself) — the full rows ARE the working set here, so no projection.
        var awards = await db.RankAwards.AsNoTracking().Include(a => a.Rank).ToListAsync(ct);
        var people = await db.Persons.AsNoTracking()
            .Where(p => !p.Archived)
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);

        var labels = await db.RankSystems.AsNoTracking().Select(s => new { s.Id, s.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var linked = await db.RankSystemProgramLinks.AsNoTracking()
            .Join(db.GymPrograms, l => l.GymProgramId, p => p.Id, (l, p) => new { l.RankSystemId, p.Title })
            .ToListAsync(ct);
        foreach (var link in linked)
        {
            labels[link.RankSystemId] = link.Title;
        }

        return [.. awards
            .Where(a => people.ContainsKey(a.PersonId))
            .GroupBy(a => (a.PersonId, a.Rank.RankSystemId))
            .Select(g => (g.Key, Current: RankProgress.Current(g)))
            .Where(x => x.Current is not null)
            .Select(x => new PipelineRow(
                x.Key.PersonId,
                people[x.Key.PersonId],
                labels.GetValueOrDefault(x.Key.RankSystemId, "—"),
                x.Current!.Rank.Name,
                x.Current.Stripes,
                x.Current.AtRankSince))
            .OrderBy(r => r.AtRankSince)];
    }

    /// <summary>Outstanding balances aged from the oldest cycle payments no longer
    /// cover (#221). Payments are person-level and unattributable (#199), so aging
    /// applies them oldest-first — never invent a per-charge allocation.</summary>
    public async Task<List<AgingRow>> DuesAgingAsync(CancellationToken ct = default)
    {
        // Aging needs every charge row (oldest-first application), but only
        // three columns of it — and payments only as per-person sums.
        var charges = await db.Charges.AsNoTracking()
            .Select(c => new { c.PersonId, c.Amount, c.RaisedOn })
            .ToListAsync(ct);
        var paidByPerson = await db.Payments.AsNoTracking()
            .Where(p => p.VoidedUtc == null)
            .GroupBy(p => p.PersonId)
            .Select(g => new { g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var people = await db.Persons.AsNoTracking()
            .Where(p => !p.Archived)
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);

        var rows = new List<AgingRow>();
        foreach (var group in charges.GroupBy(c => c.PersonId))
        {
            if (!people.TryGetValue(group.Key, out var name))
            {
                continue;
            }

            var owed = group.Sum(c => c.Amount);
            var paid = paidByPerson.GetValueOrDefault(group.Key);
            var balance = owed - paid;
            if (balance <= 0)
            {
                continue;
            }

            // Oldest-first application: walk charges until payments run out —
            // the first charge not fully covered dates the debt.
            var remaining = paid;
            var oldestUnpaid = group.OrderBy(c => c.RaisedOn).First().RaisedOn;
            foreach (var charge in group.OrderBy(c => c.RaisedOn))
            {
                if (remaining < charge.Amount)
                {
                    oldestUnpaid = charge.RaisedOn;
                    break;
                }

                remaining -= charge.Amount;
            }

            rows.Add(new AgingRow(group.Key, name, balance, oldestUnpaid));
        }

        return [.. rows.OrderBy(r => r.OldestUnpaidOn)];
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

    /// <summary>
    /// Live prefill rows for the pricing what-if (#221): one lever per billing
    /// per-person plan with its ACTUALLY billed head count (unarchived,
    /// non-visitor, not covered by a family plan — the cycle's own rules), plus
    /// one line per family-scope plan carrying its live computed total (family
    /// prices vary by composition, so the line is the sum with Members = 1).
    /// Every row stays owner-editable — starting points, not assumptions.
    /// </summary>
    public async Task<List<PlanWhatIfRow>> PricingWhatIfPrefillAsync(CancellationToken ct = default)
    {
        var plans = await db.MembershipPlans.AsNoTracking().Where(p => !p.Archived).ToListAsync(ct);

        var covered = await db.Families.AsNoTracking()
            .Where(f => f.MembershipPlanId != null)
            .Join(db.MembershipPlans.Where(p => !p.Archived && p.Scope == PlanScope.Family), f => f.MembershipPlanId, p => p.Id, (f, _) => f.Id)
            .Join(db.FamilyMembers, familyId => familyId, m => m.FamilyId, (_, m) => m.PersonId)
            .ToListAsync(ct);
        var coveredSet = covered.ToHashSet();

        var billable = await db.Persons.AsNoTracking()
            .Where(p => !p.Archived && !p.Visitor && p.MembershipPlanId != null)
            .Select(p => new { p.Id, PlanId = p.MembershipPlanId!.Value })
            .ToListAsync(ct);
        var countsByPlan = billable
            .Where(p => !coveredSet.Contains(p.Id))
            .GroupBy(p => p.PlanId)
            .ToDictionary(g => g.Key, g => g.Count());

        var rows = plans
            .Where(p => p.Scope == PlanScope.PerPerson && p.Price > 0)
            .Select(p => new PlanWhatIfRow(p.Name, p.Price, countsByPlan.GetValueOrDefault(p.Id)))
            .Where(r => r.Members > 0)
            .OrderByDescending(r => r.Revenue)
            .ToList();

        foreach (var familyPlan in plans.Where(p => p.Scope == PlanScope.Family))
        {
            var families = await db.Families.AsNoTracking()
                .Where(f => f.MembershipPlanId == familyPlan.Id)
                .Include(f => f.Members)
                .ToListAsync(ct);
            if (families.Count == 0)
            {
                continue;
            }

            var memberIds = families.SelectMany(f => f.Members.Select(m => m.PersonId)).Distinct().ToList();
            var unarchived = (await db.Persons.AsNoTracking()
                .Where(p => memberIds.Contains(p.Id) && !p.Archived)
                .Select(p => p.Id)
                .ToListAsync(ct)).ToHashSet();

            var total = 0m;
            foreach (var family in families)
            {
                var live = family.Members.Where(m => unarchived.Contains(m.PersonId)).ToList();
                var (familyTotal, _) = FamilyPlanMath.Compute(familyPlan, live.Count(m => !m.IsWard), live.Count(m => m.IsWard));
                total += familyTotal;
            }

            if (total > 0)
            {
                rows.Add(new PlanWhatIfRow($"{familyPlan.Name} ({families.Count} families, live total)", total, 1));
            }
        }

        return rows;
    }
}
