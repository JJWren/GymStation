using GymStation.Domain.People;

namespace GymStation.Domain.Tests;

/// <summary>The ADR 0003 visibility matrix: instructors only, unarchived only,
/// and never without an uploaded portrait.</summary>
public class InstructorPortraitsTests
{
    private static Person Make(PersonRoles roles, bool archived = false, string? portrait = "gyms/x/portraits/y.webp")
        => new() { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Roles = roles, Archived = archived, PortraitPath = portrait };

    [Fact]
    public void UnarchivedInstructor_WithPortrait_IsVisible()
        => Assert.True(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Instructor | PersonRoles.Member)));

    [Fact]
    public void PlainMember_IsNever()
        => Assert.False(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Member)));

    [Fact]
    public void ArchivedInstructor_RePrivatizesByConstruction()
        => Assert.False(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Instructor, archived: true)));

    [Fact]
    public void InstructorWithoutPortrait_HasNothingToServe()
        => Assert.False(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Instructor, portrait: null)));

    [Fact]
    public void StaffAdminOwner_WithoutInstructorRole_StayPrivate()
    {
        Assert.False(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Staff)));
        Assert.False(InstructorPortraits.PubliclyVisible(Make(PersonRoles.Admin | PersonRoles.Owner)));
    }
}
