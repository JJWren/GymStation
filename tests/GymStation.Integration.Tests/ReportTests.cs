using GymStation.Domain.Money;
using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Reports;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class ReportTests(PostgresFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private async Task<TenantContext> SeedAsync()
    {
        await using var setup = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = new Gym { Id = Guid.NewGuid(), Name = $"Rep {suffix}", Slug = $"rep-{suffix}", TimeZoneId = "UTC" };
        setup.Gyms.Add(gym);
        await setup.SaveChangesAsync();

        var tenant = new TenantContext();
        tenant.SetGym(gym.Id);

        await using var context = fixture.CreateContext(tenant);
        var plan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Adult", Price = 85m };
        context.MembershipPlans.Add(plan);

        var member = new Person { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "R", MembershipPlanId = plan.Id, JoinedOn = new DateOnly(2026, 1, 1) };
        context.Persons.Add(member);

        var category = new ExpenseCategory { Id = Guid.NewGuid(), Name = "RENT" };
        context.ExpenseCategories.Add(category);

        // July: collected 170, spent 100. August: collected 85, spent 40.
        context.Payments.AddRange(
            new Payment { Id = Guid.NewGuid(), PersonId = member.Id, Amount = 170m, ReceivedOn = new DateOnly(2026, 7, 10) },
            new Payment { Id = Guid.NewGuid(), PersonId = member.Id, Amount = 85m, ReceivedOn = new DateOnly(2026, 8, 3) });
        context.Expenses.AddRange(
            new Expense { Id = Guid.NewGuid(), CategoryId = category.Id, Amount = 100m, SpentOn = new DateOnly(2026, 7, 5) },
            new Expense { Id = Guid.NewGuid(), CategoryId = category.Id, Amount = 40m, SpentOn = new DateOnly(2026, 8, 5) });

        await context.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task MoneySeries_BucketsPaymentsAndExpensesByMonth()
    {
        var tenant = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var series = await new ReportService(context).MoneySeriesAsync(Today, months: 3);

        Assert.Equal(3, series.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), series[0].Month);
        Assert.Equal((0m, 0m), (series[0].Collected, series[0].Expenses));
        Assert.Equal((170m, 100m), (series[1].Collected, series[1].Expenses));
        Assert.Equal((85m, 40m), (series[2].Collected, series[2].Expenses));
    }

    [Fact]
    public async Task BreakEvenPrefill_UsesLiveRosterAndLastMonthExpenses()
    {
        var tenant = await SeedAsync();

        await using var context = fixture.CreateContext(tenant);
        var prefill = await new ReportService(context).BreakEvenPrefillAsync(Today);

        Assert.Equal(1, prefill.ActiveMembers);
        Assert.Equal(85m, prefill.Arpm);
        Assert.Equal(100m, prefill.MonthlyFixedCosts); // July's log, not August's partial month
    }
}
