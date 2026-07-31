using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

public enum SubstitutionStatus
{
    /// <summary>Waiting for the named sub to accept, or (open request) for anyone to claim.</summary>
    Requested = 0,

    /// <summary>Accepted/claimed; only exists as a resting state in admin-gated gyms.</summary>
    PendingApproval = 1,

    /// <summary>Finalized — the session's instructor has been updated.</summary>
    Applied = 2,

    Declined = 3,
    Withdrawn = 4,
}

/// <summary>
/// One instructor-cover request for one dated session. The gym's SubstitutionMode
/// (Q11) decides whether acceptance applies immediately or waits for an admin.
/// </summary>
public class SubstitutionRequest : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid SessionId { get; set; }
    public ClassSession Session { get; set; } = null!;

    public Guid RequestedByPersonId { get; set; }

    /// <summary>Named proposal; null = open request any instructor can claim (if the gym allows).</summary>
    public Guid? ProposedSubPersonId { get; set; }

    /// <summary>Who actually accepted/claimed it.</summary>
    public Guid? AcceptedByPersonId { get; set; }

    public SubstitutionStatus Status { get; set; } = SubstitutionStatus.Requested;

    public DateTimeOffset RequestedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcceptedUtc { get; set; }
    public DateTimeOffset? ResolvedUtc { get; set; }

    /// <summary>Set when the unfilled request was escalated to admins at T-24h.</summary>
    public DateTimeOffset? EscalatedUtc { get; set; }

    public string? Note { get; set; }
}
