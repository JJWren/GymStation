using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

/// <summary>
/// Mint ledger for lazy materialization (#168): one row means "this template's
/// occurrence for this week was created once — and never will be again." Moving
/// or deleting the occurrence afterwards leaves the claim standing, which is
/// exactly what stops a vacated slot from refilling as a duplicate.
/// </summary>
public class ClassTemplateWeek : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid TemplateId { get; set; }

    /// <summary>Sunday that starts the minted week (Weeks.WeekOf).</summary>
    public DateOnly WeekStart { get; set; }
}
