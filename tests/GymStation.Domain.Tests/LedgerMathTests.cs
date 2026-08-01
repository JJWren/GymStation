using GymStation.Domain.Money;

namespace GymStation.Domain.Tests;

public class LedgerMathTests
{
    private static Charge ChargeOf(decimal amount, DateOnly on) => new()
    {
        Id = Guid.NewGuid(),
        Amount = amount,
        Description = "dues",
        RaisedOn = on,
    };

    private static Payment PaymentOf(decimal amount, DateOnly on) => new()
    {
        Id = Guid.NewGuid(),
        Amount = amount,
        ReceivedOn = on,
    };

    [Fact]
    public void Balance_IsChargesMinusPayments()
    {
        var charges = new[] { ChargeOf(85, new DateOnly(2026, 6, 1)), ChargeOf(85, new DateOnly(2026, 7, 1)) };
        var payments = new[] { PaymentOf(85, new DateOnly(2026, 6, 3)) };

        Assert.Equal(85m, LedgerMath.Balance(charges, payments));
    }

    [Fact]
    public void Arrears_AppliesPaymentsToOldestChargesFirst()
    {
        var charges = new[]
        {
            ChargeOf(85, new DateOnly(2026, 5, 1)),
            ChargeOf(85, new DateOnly(2026, 6, 1)),
            ChargeOf(85, new DateOnly(2026, 7, 1)),
        };
        var payments = new[] { PaymentOf(85, new DateOnly(2026, 5, 2)) };

        var arrears = LedgerMath.Arrears(charges, payments);

        Assert.Equal(170m, arrears.Balance);
        Assert.Equal(new DateOnly(2026, 6, 1), arrears.OldestUnpaidSince);
    }

    [Fact]
    public void Arrears_PartialPayment_LeavesTheChargeUnpaid()
    {
        var charges = new[] { ChargeOf(85, new DateOnly(2026, 7, 1)) };
        var payments = new[] { PaymentOf(40, new DateOnly(2026, 7, 5)) };

        var arrears = LedgerMath.Arrears(charges, payments);

        Assert.Equal(45m, arrears.Balance);
        Assert.Equal(new DateOnly(2026, 7, 1), arrears.OldestUnpaidSince);
    }

    [Fact]
    public void Arrears_FullyPaid_HasNoUnpaidSince()
    {
        var charges = new[] { ChargeOf(85, new DateOnly(2026, 7, 1)) };
        var payments = new[] { PaymentOf(85, new DateOnly(2026, 7, 2)) };

        var arrears = LedgerMath.Arrears(charges, payments);

        Assert.Equal(0m, arrears.Balance);
        Assert.Null(arrears.OldestUnpaidSince);
    }

    [Fact]
    public void Arrears_Overpayment_YieldsCreditBalance()
    {
        var charges = new[] { ChargeOf(85, new DateOnly(2026, 7, 1)) };
        var payments = new[] { PaymentOf(100, new DateOnly(2026, 7, 2)) };

        var arrears = LedgerMath.Arrears(charges, payments);

        Assert.Equal(-15m, arrears.Balance);
        Assert.Null(arrears.OldestUnpaidSince);
    }
}
