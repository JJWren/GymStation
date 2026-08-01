using GymStation.Domain.People;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Money;

/// <summary>
/// A recorded settlement against a Person's Charges. v1 records money the gym collected
/// elsewhere (cash, card reader, transfer) — processing stays on the future board.
/// </summary>
public class Payment : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public Guid? RecordedByPersonId { get; set; }
    public string? Note { get; set; }
}
