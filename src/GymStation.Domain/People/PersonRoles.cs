namespace GymStation.Domain.People;

/// <summary>
/// A Person's roles at their gym — a set, never exclusive (ADR 0002).
/// The coach who trains holds Instructor | Member on one Person.
/// </summary>
[Flags]
public enum PersonRoles
{
    None = 0,
    Member = 1,
    Instructor = 2,
    Admin = 4,
    Owner = 8,

    /// <summary>General staff (front desk, cleaning, ops): payable and profiled like
    /// an instructor, but with zero portal permissions — GymStaff stays Admin|Owner.</summary>
    Staff = 16,
}
