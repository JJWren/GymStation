namespace GymStation.Domain.Money;

public record ArrearsInfo(decimal Balance, DateOnly? OldestUnpaidSince);

/// <summary>
/// Pure ledger derivations. Balance = ΣCharges − ΣPayments, always derived, never edited.
/// Payments apply to charges oldest-first (FIFO) to find how long the member has been behind.
/// </summary>
public static class LedgerMath
{
    public static decimal Balance(IEnumerable<Charge> charges, IEnumerable<Payment> payments)
        => charges.Sum(c => c.Amount) - payments.Sum(p => p.Amount);

    public static ArrearsInfo Arrears(IEnumerable<Charge> charges, IEnumerable<Payment> payments)
    {
        var ordered = charges.OrderBy(c => c.RaisedOn).ToList();
        var remainingPaid = payments.Sum(p => p.Amount);
        var balance = ordered.Sum(c => c.Amount) - remainingPaid;

        foreach (var charge in ordered)
        {
            if (remainingPaid >= charge.Amount)
            {
                remainingPaid -= charge.Amount;
                continue;
            }

            // First charge not fully covered — the member has been behind since it was raised.
            return new ArrearsInfo(balance, charge.RaisedOn);
        }

        return new ArrearsInfo(balance, null);
    }
}
