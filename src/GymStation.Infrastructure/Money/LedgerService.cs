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

        var alreadyCharged = (await db.Charges
                .Where(c => c.CycleKey == cycleKey)
                .Select(c => c.PersonId)
                .ToListAsync(ct))
            .ToHashSet();

        var raised = 0;
        foreach (var row in planned.Where(x => !alreadyCharged.Contains(x.Person.Id)))
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

        if (raised > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent cycle run inserted the same (person, cycle) rows; the unique
                // index kept the ledger correct.
                db.ChangeTracker.Clear();
                return 0;
            }
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
        var payments = await db.Payments.ToListAsync(ct);
        var personIds = charges.Select(c => c.PersonId).Distinct().ToList();
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

    public async Task<(decimal CollectedInMonth, decimal Outstanding, int MembersCurrent, int MembersBehind)> MonthSummaryAsync(
        DateOnly month, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var collected = await db.Payments
            .Where(p => p.ReceivedOn >= monthStart && p.ReceivedOn <= monthEnd)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var dues = await DuesAsync(ct);
        var chargedPeople = await db.Charges.Select(c => c.PersonId).Distinct().CountAsync(ct);

        return (collected, dues.Sum(d => d.Balance), chargedPeople - dues.Count, dues.Count);
    }
}
