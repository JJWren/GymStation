using GymStation.Domain.Money;
using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Money;
using GymStation.Infrastructure.Notifications;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class LedgerTests(PostgresFixture fixture)
{
    private async Task<(TenantContext Tenant, Person Planned, Person Unplanned, MembershipPlan Plan)> SeedAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Ledger {suffix}", Slug = $"ledger-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        var plan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Adult Unlimited", Price = 85m };
        context.MembershipPlans.Add(plan);

        var planned = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Ana",
            LastName = "R",
            UserId = Guid.NewGuid(),
            MembershipPlanId = plan.Id,
            JoinedOn = new DateOnly(2026, 1, 1),
        };
        var unplanned = new Person { Id = Guid.NewGuid(), FirstName = "Leo", LastName = "P", JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.AddRange(planned, unplanned);
        await context.SaveChangesAsync();

        return (tenant, planned, unplanned, plan);
    }

    [Fact]
    public async Task MonthlyCycle_ChargesPlannedMembersOnce_AndIsIdempotent()
    {
        var (tenant, planned, unplanned, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));
        var month = new DateOnly(2026, 8, 1);

        var first = await ledger.RaiseMonthlyChargesAsync(month);
        var second = await ledger.RaiseMonthlyChargesAsync(month);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(await context.Charges.Where(c => c.PersonId == planned.Id).ToListAsync());
        Assert.Empty(await context.Charges.Where(c => c.PersonId == unplanned.Id).ToListAsync());
    }

    [Fact]
    public async Task Payment_ClearsDues_AndDuesListOrdersOldestFirst()
    {
        var (tenant, planned, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));
        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 7, 1));
        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 8, 1));

        var dues = await ledger.DuesAsync();
        Assert.Single(dues);
        Assert.Equal(170m, dues[0].Balance);
        Assert.Equal(new DateOnly(2026, 7, 1), dues[0].OldestUnpaidSince);

        await ledger.RecordPaymentAsync(planned.Id, 170m, new DateOnly(2026, 8, 2), null, null);

        Assert.Empty(await ledger.DuesAsync());
        Assert.Equal(0m, (await ledger.ArrearsForAsync(planned.Id)).Balance);
    }

    [Fact]
    public async Task RecordPayment_RejectsNonPositiveAmounts()
    {
        var (tenant, planned, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ledger.RecordPaymentAsync(planned.Id, 0m, new DateOnly(2026, 8, 2), null, null));
    }

    [Fact]
    public async Task CycleCharge_NotifiesTheMemberInApp()
    {
        var (tenant, planned, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));
        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 8, 1));

        var notification = await context.Notifications.SingleAsync();
        Assert.Equal(planned.UserId, notification.RecipientUserId);
        Assert.Empty(notification.Deliveries); // in-app only; no email spam for routine dues
    }

    [Fact]
    public async Task Charges_AreTenantScoped()
    {
        var (tenantA, _, _, _) = await SeedAsync();
        var (tenantB, _, _, _) = await SeedAsync();

        await using (var contextA = fixture.CreateContext(tenantA))
        {
            await new LedgerService(contextA, new NotificationService(contextA)).RaiseMonthlyChargesAsync(new DateOnly(2026, 8, 1));
        }

        // Gym B never ran a cycle — gym A's charges must be invisible to it.
        await using var contextB = fixture.CreateContext(tenantB);
        Assert.Empty(await contextB.Charges.ToListAsync());
    }

    [Fact]
    public async Task VoidingAPayment_RestoresTheBalance_WithAuditTrail()
    {
        var (tenant, planned, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));
        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 8, 1));
        await ledger.RecordPaymentAsync(planned.Id, 85m, new DateOnly(2026, 8, 2), null, null);

        Assert.Equal(0m, (await ledger.ArrearsForAsync(planned.Id)).Balance);

        var payment = await context.Payments.SingleAsync(p => p.PersonId == planned.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.VoidPaymentAsync(payment.Id, null, "  "));

        await ledger.VoidPaymentAsync(payment.Id, null, "wrong member");
        Assert.Equal(85m, (await ledger.ArrearsForAsync(planned.Id)).Balance);
        Assert.Contains(await ledger.DuesAsync(), d => d.PersonId == planned.Id);

        var (collected, _, _, _) = await ledger.MonthSummaryAsync(new DateOnly(2026, 8, 1));
        Assert.Equal(0m, collected);

        // Voiding twice is refused; the row itself survives with its audit trail.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.VoidPaymentAsync(payment.Id, null, "again"));
        Assert.Equal("wrong member", (await context.Payments.SingleAsync(p => p.Id == payment.Id)).VoidReason);
    }

    [Fact]
    public async Task RecurringExpenses_MaterializeOncePerMonth()
    {
        var (tenant, _, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var category = new ExpenseCategory { Id = Guid.NewGuid(), Name = "RENT" };
        context.ExpenseCategories.Add(category);
        await context.SaveChangesAsync();

        var ledger = new LedgerService(context, new NotificationService(context));
        await ledger.AddRecurringExpenseAsync(category.Id, 4200m, 1, null);

        Assert.Equal(1, await ledger.MaterializeRecurringExpensesAsync(new DateOnly(2026, 8, 15)));
        Assert.Equal(0, await ledger.MaterializeRecurringExpensesAsync(new DateOnly(2026, 8, 20)));

        var expense = await context.Expenses.SingleAsync(x => x.RecurringExpenseId != null);
        Assert.Equal(new DateOnly(2026, 8, 1), expense.SpentOn);
        Assert.Equal(4200m, expense.Amount);

        // Paused recurrings stop materializing in later months.
        var recurring = await context.RecurringExpenses.SingleAsync();
        await ledger.SetRecurringActiveAsync(recurring.Id, false);
        Assert.Equal(0, await ledger.MaterializeRecurringExpensesAsync(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public async Task ArchivedPlans_StopFutureCycles_AndPriceEditsAffectFutureOnly()
    {
        var (tenant, planned, _, plan) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var ledger = new LedgerService(context, new NotificationService(context));

        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 8, 1));
        await ledger.UpdatePlanAsync(plan.Id, "Adult Unlimited", 95m);
        await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 9, 1));

        var charges = await context.Charges.Where(c => c.PersonId == planned.Id).OrderBy(c => c.RaisedOn).ToListAsync();
        Assert.Equal(85m, charges[0].Amount); // history untouched
        Assert.Equal(95m, charges[1].Amount); // future cycle uses the new price

        await ledger.SetPlanArchivedAsync(plan.Id, true);
        Assert.Equal(0, await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task ZeroPricePlans_AreCompedAndRaiseNothing()
    {
        var (tenant, planned, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var comped = new MembershipPlan { Id = Guid.NewGuid(), Name = "Comped Instructors", Price = 0m };
        context.MembershipPlans.Add(comped);
        (await context.Persons.SingleAsync(p => p.Id == planned.Id)).MembershipPlanId = comped.Id;
        await context.SaveChangesAsync();

        var ledger = new LedgerService(context, new NotificationService(context));
        var raised = await ledger.RaiseMonthlyChargesAsync(new DateOnly(2026, 9, 1));

        Assert.Equal(0, raised);
        Assert.Empty(await context.Charges.Where(c => c.CycleKey == "2026-09").ToListAsync());
    }
}
