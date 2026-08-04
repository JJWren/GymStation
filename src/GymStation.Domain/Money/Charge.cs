using GymStation.Domain.People;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Money;

/// <summary>An amount a Person owes the gym — raised per plan cycle or ad hoc.</summary>
public class Charge : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public DateOnly RaisedOn { get; set; }

    /// <summary>"yyyy-MM" for cycle charges (unique per person per cycle); null for ad hoc.</summary>
    public string? CycleKey { get; set; }

    /// <summary>
    /// Family-size breakdown, stamped at raise time on family-plan charges (#181):
    /// the counted non-ward members / wards and the extras portion of Amount
    /// (base reconstructs as Amount − FamilyExtraAmount). Null on individual and
    /// ad-hoc charges — and on family charges raised before sizing existed.
    /// History stays immutable; later roster changes never restate these.
    /// </summary>
    public int? FamilyAdults { get; set; }

    /// <inheritdoc cref="FamilyAdults" />
    public int? FamilyKids { get; set; }

    /// <inheritdoc cref="FamilyAdults" />
    public decimal? FamilyExtraAmount { get; set; }
}
