using GymStation.Domain.Money;
using GymStation.Domain.Notifications;
using GymStation.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Money;

public record DuesRow(Guid PersonId, string Name, decimal Balance, DateOnly OldestUnpaidSince);

public class LedgerService(GymStationDbContext db, NotificationService notifications)
{
    /// <summary>
    /// Raises the month's cycle charges for every active Person on a Monthly plan.
    /// Idempotent: the unique (GymId, PersonId, CycleKey) index backstops the pre-check,
    /// so re-running a cycle never double-charges. Returns the number raised.
    /// </summary>
    public async Task<int> RaiseMonthlyChargesAsync(DateOnly cycleMonth, CancellationToken ct = default)
    {
        var cycleKey = $"{cycleMonth:yyyy-MM}";
        var raisedOn = new DateOnly(cycleMonth.Year, cycleMonth.Month, 1);

        var planned = await db.Persons
            .Where(p => !p.Archived && p.MembershipPlanId != null)
            .Join(db.MembershipPlans.Where(pl => !pl.Archived && pl.Cadence == PlanCadence.Monthly),
                p => p.MembershipPlanId, pl => pl.Id, (p, pl) => new { Person = p, Plan = pl })
            .ToListAsync(ct);

        // Members of a family on an ACTIVE family-scope plan are covered — the family
        // charge on the primary's Person replaces their individual one (#91). The scope
        // filter matters: a family mispointed at a per-person or archived plan must not
        // shield its members from the individual cycle while the family pass bills
        // nothing. (A price-0 family plan still covers: that's a comped family, the
        // same shape as comped individual plans.)
        var familyCoveredPersonIds = (await db.FamilyMembers
                .Join(db.Families.Where(f => f.MembershipPlanId != null), m => m.FamilyId, f => f.Id, (m, f) => new { m.PersonId, f.MembershipPlanId })
                .Join(db.MembershipPlans.Where(pl => !pl.Archived && pl.Scope == PlanScope.Family),
                    x => x.MembershipPlanId, pl => pl.Id, (x, pl) => x.PersonId)
                .ToListAsync(ct))
            .ToHashSet();

        var alreadyCharged = (await db.Charges
                .Where(c => c.CycleKey == cycleKey)
                .Select(c => c.PersonId)
                .ToListAsync(ct))
            .ToHashSet();

        var raised = 0;

        // Zero-price plans are comped (instructors, staff) — no charges, no notifications.
        foreach (var row in planned.Where(x => !alreadyCharged.Contains(x.Person.Id)
            && !familyCoveredPersonIds.Contains(x.Person.Id) && x.Plan.Price > 0))
        {
            db.Charges.Add(new Charge
            {
                Id = Guid.NewGuid(),
                PersonId = row.Person.Id,
                Amount = row.Plan.Price,
                Description = $"{row.Plan.Name} · {cycleKey}",
                RaisedOn = raisedOn,
                CycleKey = cycleKey,
            });

            if (row.Person.UserId is { } userId)
            {
                notifications.Notify(
                    [userId],
                    NotificationCategory.ChargeRaised,
                    $"Dues raised: {row.Plan.Name} · {cycleKey}",
                    $"Your {row.Plan.Name} dues of {row.Plan.Price:C} for {cycleMonth:MMMM yyyy} were raised. Pay however your gym collects — the ledger updates when staff record it.",
                    "/schedule",
                    email: false);
            }

            raised++;
        }

        raised += await RaiseFamilyChargesAsync(cycleMonth, raisedOn, ct);

        if (raised > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Unique-violation only: a concurrent cycle run inserted the same
                // (person, cycle) rows and the index kept the ledger correct.
                // Anything else (FK, truncation) surfaces.
                db.ChangeTracker.Clear();
                return 0;
            }
        }

        return raised;
    }

    /// <summary>
    /// One charge per family plan per month, on the PRIMARY guardian's own roster
    /// Person (#91). The cycle key carries the family id so a primary who also has a
    /// personal plan never collides on the (person, cycle) unique index.
    /// </summary>
    private async Task<int> RaiseFamilyChargesAsync(DateOnly cycleMonth, DateOnly raisedOn, CancellationToken ct)
    {
        // No Price filter here (#181): a $0-base plan with per-head extras still
        // charges sized families. "Comped" is decided per family below, on the
        // COMPUTED total.
        var families = await db.Families
            .Where(f => f.MembershipPlanId != null)
            .Join(db.MembershipPlans.Where(pl =>
                    !pl.Archived && pl.Scope == PlanScope.Family && pl.Cadence == PlanCadence.Monthly),
                f => f.MembershipPlanId, pl => pl.Id, (f, pl) => new { Family = f, Plan = pl })
            .ToListAsync(ct);
        if (families.Count == 0)
        {
            return 0;
        }

        var familyIds = families.Select(x => x.Family.Id).ToList();

        // Size sampled at raise time (#181): unarchived members only, split by
        // wardship (kid = ward, ADR 0005). Later joins/graduations reprice the
        // NEXT cycle — raised charges are history.
        var memberCounts = await db.FamilyMembers
            .Where(m => familyIds.Contains(m.FamilyId))
            .Join(db.Persons.Where(p => !p.Archived), m => m.PersonId, p => p.Id, (m, p) => new { m.FamilyId, m.IsWard })
            .GroupBy(m => new { m.FamilyId, m.IsWard })
            .Select(g => new { g.Key.FamilyId, g.Key.IsWard, Count = g.Count() })
            .ToListAsync(ct);
        var adultCounts = memberCounts.Where(x => !x.IsWard).ToDictionary(x => x.FamilyId, x => x.Count);
        var kidCounts = memberCounts.Where(x => x.IsWard).ToDictionary(x => x.FamilyId, x => x.Count);

        // One round trip for this cycle's existing family keys — no per-family AnyAsync.
        var monthPrefix = $"{cycleMonth:yyyy-MM}:family:";
        var existingKeys = (await db.Charges
                .Where(c => c.CycleKey != null && c.CycleKey.StartsWith(monthPrefix))
                .Select(c => c.CycleKey!)
                .ToListAsync(ct))
            .ToHashSet();
        var primaries = await db.FamilyGuardians
            .Where(g => familyIds.Contains(g.FamilyId) && g.IsPrimary)
            .ToListAsync(ct);
        var primaryUserIds = primaries.Select(p => p.GuardianUserId).Distinct().ToList();
        var primaryPersons = await db.Persons
            .Where(p => !p.Archived && p.UserId != null && primaryUserIds.Contains(p.UserId.Value))
            .ToDictionaryAsync(p => p.UserId!.Value, ct);

        var raised = 0;
        foreach (var row in families)
        {
            var familyCycleKey = $"{monthPrefix}{row.Family.Id}";
            if (existingKeys.Contains(familyCycleKey))
            {
                continue;
            }

            // "Primary must have a roster Person" — a family without one just skips;
            // staff see the gap on the family page and repair it.
            var primary = primaries.SingleOrDefault(p => p.FamilyId == row.Family.Id);
            if (primary is null || !primaryPersons.TryGetValue(primary.GuardianUserId, out var person))
            {
                continue;
            }

            var adults = adultCounts.GetValueOrDefault(row.Family.Id);
            var kids = kidCounts.GetValueOrDefault(row.Family.Id);
            var (total, extraAmount) = FamilyPlanMath.Compute(row.Plan, adults, kids);
            if (total <= 0)
            {
                continue; // computed-comped (#181): zero total raises nothing, like comped individuals
            }

            db.Charges.Add(new Charge
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                Amount = total,
                Description = $"{row.Plan.Name} · {row.Family.Name} · {cycleMonth:yyyy-MM}",
                RaisedOn = raisedOn,
                CycleKey = familyCycleKey,
                FamilyAdults = adults,
                FamilyKids = kids,
                FamilyExtraAmount = extraAmount,
            });

            notifications.Notify(
                [primary.GuardianUserId],
                NotificationCategory.ChargeRaised,
                $"Family dues raised: {row.Plan.Name} · {cycleMonth:yyyy-MM}",
                $"{row.Family.Name}'s {row.Plan.Name} dues of {total:C} for {cycleMonth:MMMM yyyy} were raised on your account. Pay however your gym collects — the ledger updates when staff record it.",
                "/family",
                email: false);

            raised++;
        }

        return raised;
    }

    public async Task RecordPaymentAsync(
        Guid personId, decimal amount, DateOnly receivedOn, Guid? recordedByPersonId, string? note, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("A payment must be a positive amount.");
        }

        _ = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new InvalidOperationException("Person not found in the active gym.");

        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            Amount = amount,
            ReceivedOn = receivedOn,
            RecordedByPersonId = recordedByPersonId,
            Note = note,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<ArrearsInfo> ArrearsForAsync(Guid personId, CancellationToken ct = default)
    {
        var charges = await db.Charges.Where(c => c.PersonId == personId).ToListAsync(ct);
        var payments = await db.Payments.Where(p => p.PersonId == personId).ToListAsync(ct);
        return LedgerMath.Arrears(charges, payments);
    }

    /// <summary>Members behind on dues, oldest arrears first.</summary>
    public async Task<List<DuesRow>> DuesAsync(CancellationToken ct = default)
    {
        var charges = await db.Charges.ToListAsync(ct);
        if (charges.Count == 0)
        {
            return [];
        }

        var personIds = charges.Select(c => c.PersonId).Distinct().ToList();
        var payments = await db.Payments.Where(p => personIds.Contains(p.PersonId)).ToListAsync(ct);
        var names = await db.Persons.Where(p => personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        return charges.GroupBy(c => c.PersonId)
            .Select(g =>
            {
                var arrears = LedgerMath.Arrears(g, payments.Where(p => p.PersonId == g.Key));
                return (g.Key, arrears);
            })
            .Where(x => x.arrears.Balance > 0 && x.arrears.OldestUnpaidSince is not null)
            .Select(x => new DuesRow(x.Key, names.GetValueOrDefault(x.Key, "?"), x.arrears.Balance, x.arrears.OldestUnpaidSince!.Value))
            .OrderBy(r => r.OldestUnpaidSince)
            .ToList();
    }

    public async Task<(decimal CollectedInMonth, decimal Outstanding, int MembersCurrent, int MembersBehind, decimal OtherIncomeInMonth)> MonthSummaryAsync(
        DateOnly month, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var collected = await db.Payments
            .Where(p => p.VoidedUtc == null && p.ReceivedOn >= monthStart && p.ReceivedOn <= monthEnd)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var otherIncome = await db.OtherIncomes
            .Where(x => x.ReceivedOn >= monthStart && x.ReceivedOn <= monthEnd)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        var dues = await DuesAsync(ct);
        var chargedPeople = await db.Charges.Select(c => c.PersonId).Distinct().CountAsync(ct);

        return (collected, dues.Sum(d => d.Balance), chargedPeople - dues.Count, dues.Count, otherIncome);
    }

    /// <summary>Voids (never deletes) a payment with an audit trail; every derived number drops it.</summary>
    public async Task VoidPaymentAsync(Guid paymentId, Guid? voidedByPersonId, string reason, CancellationToken ct = default)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length == 0)
        {
            throw new InvalidOperationException("A void needs a reason — it stays in the audit trail.");
        }

        if (reason.Length > 300)
        {
            throw new InvalidOperationException("Keep the void reason under 300 characters.");
        }

        var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new InvalidOperationException("Payment not found in the active gym.");

        if (payment.Voided)
        {
            throw new InvalidOperationException("This payment is already voided.");
        }

        payment.VoidedUtc = DateTimeOffset.UtcNow;
        payment.VoidedByPersonId = voidedByPersonId;
        payment.VoidReason = reason;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Materializes this month's due recurring expenses (rent, insurance) into real
    /// Expense rows. Idempotent per (recurring, date) via the unique index.
    /// </summary>
    public async Task<int> MaterializeRecurringExpensesAsync(DateOnly gymToday, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(gymToday.Year, gymToday.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(gymToday.Year, gymToday.Month);

        var active = await db.RecurringExpenses.Where(r => r.Active).ToListAsync(ct);
        if (active.Count == 0)
        {
            return 0;
        }

        var created = 0;
        var attempted = new List<Guid>();

        // The high-water mark is the idempotency, not row existence: an owner who
        // deletes this month's materialized rent must not have it resurrected on
        // the next worker pass (#88).
        foreach (var recurring in active.Where(r => r.LastMaterializedMonth is null || r.LastMaterializedMonth < monthStart))
        {
            var spentOn = new DateOnly(gymToday.Year, gymToday.Month, Math.Clamp(recurring.DayOfMonth, 1, daysInMonth));
            if (spentOn > gymToday)
            {
                continue; // not due yet this month
            }

            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                CategoryId = recurring.CategoryId,
                Amount = recurring.Amount,
                SpentOn = spentOn,
                Note = recurring.Note ?? "Recurring",
                RecurringExpenseId = recurring.Id,
            });
            recurring.LastMaterializedMonth = monthStart;
            attempted.Add(recurring.Id);
            created++;
        }

        if (created > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // The (recurring, spentOn) row already exists — a concurrent pass, or
                // legacy data the backfill didn't cover. The high-water mark must still
                // advance or every future pass re-collides on the same row, so persist
                // the marks alone, straight to the database.
                db.ChangeTracker.Clear();
                await db.RecurringExpenses
                    .Where(r => attempted.Contains(r.Id)
                        && (r.LastMaterializedMonth == null || r.LastMaterializedMonth < monthStart))
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.LastMaterializedMonth, monthStart), ct);
                return 0;
            }
        }

        return created;
    }

    public async Task UpdatePlanAsync(
        Guid planId, string name, decimal price,
        int? includedAdults = null, int? includedKids = null,
        decimal? extraAdultPrice = null, decimal? extraKidPrice = null,
        CancellationToken ct = default)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A plan name is required.");
        }

        if (name.Length > 80)
        {
            throw new InvalidOperationException("Keep the plan name under 80 characters.");
        }

        if (price < 0)
        {
            throw new InvalidOperationException("Price can't be negative — use 0 for a comped plan.");
        }

        if (includedAdults < 0 || includedKids < 0 || extraAdultPrice < 0 || extraKidPrice < 0)
        {
            throw new InvalidOperationException("Family sizing can't be negative — use 0 to switch a lane off.");
        }

        var plan = await db.MembershipPlans.SingleOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new InvalidOperationException("Plan not found in the active gym.");

        if (await db.MembershipPlans.AnyAsync(p => p.Id != planId && p.Name == name, ct))
        {
            throw new InvalidOperationException($"A plan named '{name}' already exists.");
        }

        // Price changes affect FUTURE cycles only — raised charges are history.
        plan.Name = name;
        plan.Price = price;

        // Sizing (#181) applies to family-scope plans; null = leave unchanged
        // (per-person rows never post these fields).
        if (plan.Scope == PlanScope.Family)
        {
            plan.IncludedAdults = includedAdults ?? plan.IncludedAdults;
            plan.IncludedKids = includedKids ?? plan.IncludedKids;
            plan.ExtraAdultPrice = extraAdultPrice ?? plan.ExtraAdultPrice;
            plan.ExtraKidPrice = extraKidPrice ?? plan.ExtraKidPrice;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPlanArchivedAsync(Guid planId, bool archived, CancellationToken ct = default)
    {
        var plan = await db.MembershipPlans.SingleOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new InvalidOperationException("Plan not found in the active gym.");
        plan.Archived = archived;
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameCategoryAsync(Guid categoryId, string name, CancellationToken ct = default)
    {
        name = name.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A category name is required.");
        }

        if (name.Length > 60)
        {
            throw new InvalidOperationException("Keep the category name under 60 characters.");
        }

        var category = await db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new InvalidOperationException("Category not found in the active gym.");

        if (await db.ExpenseCategories.AnyAsync(c => c.Id != categoryId && c.Name == name, ct))
        {
            throw new InvalidOperationException($"A category named '{name}' already exists.");
        }

        category.Name = name;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetCategoryArchivedAsync(Guid categoryId, bool archived, CancellationToken ct = default)
    {
        var category = await db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new InvalidOperationException("Category not found in the active gym.");
        category.Archived = archived;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddRecurringExpenseAsync(Guid categoryId, decimal amount, int dayOfMonth, string? note, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("The amount must be positive.");
        }

        if (dayOfMonth is < 1 or > 31)
        {
            throw new InvalidOperationException("Day of month must be between 1 and 31.");
        }

        note = note?.Trim();
        if (note is { Length: > 300 })
        {
            throw new InvalidOperationException("Keep the note under 300 characters.");
        }

        _ = await db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new InvalidOperationException("Category not found in the active gym.");

        db.RecurringExpenses.Add(new RecurringExpense
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Amount = amount,
            DayOfMonth = dayOfMonth,
            Note = note,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetRecurringActiveAsync(Guid recurringId, bool active, CancellationToken ct = default)
    {
        var recurring = await db.RecurringExpenses.SingleOrDefaultAsync(r => r.Id == recurringId, ct)
            ?? throw new InvalidOperationException("Recurring expense not found in the active gym.");
        recurring.Active = active;
        await db.SaveChangesAsync(ct);
    }

    // Expenses and other income are the owner's own bookkeeping — fully editable and
    // deletable (Joshua explicit, #88). Dues PAYMENTS stay void-only: they involve a
    // second party and need the audit trail.

    public async Task UpdateExpenseAsync(
        Guid expenseId, Guid categoryId, decimal amount, DateOnly spentOn, string? note, CancellationToken ct = default)
    {
        ValidateMoney(amount, note);
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == expenseId, ct)
            ?? throw new InvalidOperationException("Expense not found in the active gym.");
        _ = await db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new InvalidOperationException("Category not found in the active gym.");

        expense.CategoryId = categoryId;
        expense.Amount = amount;
        expense.SpentOn = spentOn;
        expense.Note = note?.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteExpenseAsync(Guid expenseId, CancellationToken ct = default)
    {
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == expenseId, ct)
            ?? throw new InvalidOperationException("Expense not found in the active gym.");
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OtherIncome> AddOtherIncomeAsync(
        string label, decimal amount, DateOnly receivedOn, string? note, CancellationToken ct = default)
    {
        label = NormalizeIncomeLabel(label);
        ValidateMoney(amount, note);

        var income = new OtherIncome
        {
            Id = Guid.NewGuid(),
            Label = label,
            Amount = amount,
            ReceivedOn = receivedOn,
            Note = note?.Trim(),
        };
        db.OtherIncomes.Add(income);
        await db.SaveChangesAsync(ct);
        return income;
    }

    public async Task UpdateOtherIncomeAsync(
        Guid incomeId, string label, decimal amount, DateOnly receivedOn, string? note, CancellationToken ct = default)
    {
        label = NormalizeIncomeLabel(label);
        ValidateMoney(amount, note);
        var income = await db.OtherIncomes.SingleOrDefaultAsync(x => x.Id == incomeId, ct)
            ?? throw new InvalidOperationException("Income entry not found in the active gym.");

        income.Label = label;
        income.Amount = amount;
        income.ReceivedOn = receivedOn;
        income.Note = note?.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteOtherIncomeAsync(Guid incomeId, CancellationToken ct = default)
    {
        var income = await db.OtherIncomes.SingleOrDefaultAsync(x => x.Id == incomeId, ct)
            ?? throw new InvalidOperationException("Income entry not found in the active gym.");
        db.OtherIncomes.Remove(income);
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeIncomeLabel(string label)
    {
        label = label?.Trim().ToUpperInvariant() ?? "";
        if (label.Length == 0)
        {
            throw new InvalidOperationException("An income label is required.");
        }

        if (label.Length > 60)
        {
            throw new InvalidOperationException("Keep the income label under 60 characters.");
        }

        return label;
    }

    private static void ValidateMoney(decimal amount, string? note)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("The amount must be positive.");
        }

        if (note is { Length: > 300 })
        {
            throw new InvalidOperationException("Keep the note under 300 characters.");
        }
    }
}
