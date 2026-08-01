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
