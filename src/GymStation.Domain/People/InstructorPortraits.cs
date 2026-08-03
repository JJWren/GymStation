namespace GymStation.Domain.People;

/// <summary>
/// The one rule for the public portrait endpoint (#137, ADR 0003): portraits are
/// staff-only EXCEPT for unarchived Instructor-role persons — instructors are the
/// Gym's public faces. Role loss or archiving re-privatizes by construction.
/// </summary>
public static class InstructorPortraits
{
    public static bool PubliclyVisible(Person person)
        => !person.Archived
           && person.Roles.HasFlag(PersonRoles.Instructor)
           && person.PortraitPath is not null;
}
