using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

public enum SessionStatus
{
    Scheduled = 0,
    Cancelled = 1,
}

/// <summary>
/// A dated occurrence — the unit of check-in, substitution, and cancellation.
/// Materialized lazily from its template (fields copied so later template edits
/// never rewrite history); TemplateId null = one-off session (seminar, special).
/// </summary>
public class ClassSession : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid? TemplateId { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; }

    public required string Name { get; set; }

    /// <summary>The instructor actually teaching this date (substitutions update this).</summary>
    public Guid? InstructorPersonId { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
    public string? CancelledReason { get; set; }

    public List<ClassType> ClassTypes { get; set; } = [];

    public TimeOnly EndTime => StartTime.AddMinutes(DurationMinutes);
}
