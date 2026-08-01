using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Attendance;

public enum CheckInSource
{
    Self = 0,
    Guardian = 1,
    Instructor = 2,
}

public enum AttendanceStatus
{
    /// <summary>A claim of presence, visible on the instructor's roll.</summary>
    Pending = 0,

    /// <summary>Counts toward gym-verified stats. Auto-set at session end + 2h unless amended.</summary>
    Confirmed = 1,

    /// <summary>Struck by an instructor/admin (no-show or mistake). Never counts.</summary>
    Removed = 2,
}

/// <summary>One Person × ClassSession. Soft approval (Q12): pending records auto-confirm.</summary>
public class AttendanceRecord : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid SessionId { get; set; }
    public ClassSession Session { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public CheckInSource Source { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Pending;

    public DateTimeOffset CheckedInUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedUtc { get; set; }
}
