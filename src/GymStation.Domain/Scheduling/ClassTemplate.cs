using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

/// <summary>
/// A recurring weekly slot ("Tue 18:00 No-Gi, Coach Ana"). Editing a template changes
/// the future pattern; dated changes (cancellation, substitution) live on ClassSession.
/// </summary>
public class ClassTemplate : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Name { get; set; }

    public DayOfWeek Day { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; } = 60;

    public Guid? DefaultInstructorPersonId { get; set; }

    public bool Active { get; set; } = true;

    public List<ClassType> ClassTypes { get; set; } = [];
}
