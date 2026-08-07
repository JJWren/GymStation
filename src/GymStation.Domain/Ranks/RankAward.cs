using GymStation.Domain.People;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Ranks;

/// <summary>
/// A dated promotion or stripe grant. The full award history is the source of truth:
/// current rank and "time at belt" are always derived, never stored.
/// </summary>
public class RankAward : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid RankId { get; set; }
    public Rank Rank { get; set; } = null!;

    /// <summary>Stripe count held as of this award (0 on a fresh belt promotion).</summary>
    public int Stripes { get; set; }

    public DateOnly AwardedOn { get; set; }

    /// <summary>Who recorded/awarded it; null for self-reported pre-app history.</summary>
    public Guid? AwardedByPersonId { get; set; }

    /// <summary>True for history the member entered themselves (previous gym, pre-app).</summary>
    public bool SelfReported { get; set; }

    public string? Note { get; set; }

    /// <summary>Stable tiebreaker for same-day awards (belt then stripe on promotion day).</summary>
    public DateTimeOffset RecordedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Soft delete (#220): a data-entry correction, not history editing.
    /// The global query filter hides deleted awards from every reader and current
    /// rank recomputes; the row itself stays as the audit trail of who removed
    /// what, when.</summary>
    public DateTimeOffset? DeletedUtc { get; set; }

    public Guid? DeletedByPersonId { get; set; }
}
