using GymStation.Domain.People;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Tests;

public class PersonTests
{
    [Fact]
    public void DualRolePerson_HoldsBothRolesOnOneRecord()
    {
        var coachWhoTrains = new Person
        {
            FirstName = "Rui",
            LastName = "Silva",
            Roles = PersonRoles.Instructor | PersonRoles.Member,
        };

        Assert.True(coachWhoTrains.HasRole(PersonRoles.Instructor));
        Assert.True(coachWhoTrains.HasRole(PersonRoles.Member));
        Assert.False(coachWhoTrains.HasRole(PersonRoles.Admin));
        Assert.False(coachWhoTrains.HasRole(PersonRoles.Owner));
    }

    [Fact]
    public void HasRole_NoneIsNeverHeld()
    {
        var person = new Person { FirstName = "Ana", LastName = "Reyes" };

        Assert.False(person.HasRole(PersonRoles.None));
    }

    [Fact]
    public void NewPerson_DefaultsToMemberRole()
    {
        var person = new Person { FirstName = "Ana", LastName = "Reyes" };

        Assert.Equal(PersonRoles.Member, person.Roles);
    }

    [Fact]
    public void DisplayName_ComposesFirstAndLast()
    {
        var person = new Person { FirstName = "Ana", LastName = "Reyes" };

        Assert.Equal("Ana Reyes", person.DisplayName);
    }

    [Fact]
    public void PersonWithoutUser_IsValid()
    {
        var kid = new Person { FirstName = "Leo", LastName = "Park" };

        Assert.Null(kid.UserId);
    }
}

public class GymSettingsTests
{
    [Fact]
    public void NewGymSettings_UseDecidedDefaults()
    {
        var settings = new GymSettings();

        Assert.Equal(SubstitutionMode.AutoApply, settings.SubstitutionMode);
        Assert.True(settings.OpenClaimsEnabled);
        Assert.Equal(60, settings.CheckInWindowMinutes);
        Assert.True(settings.DefaultThemeDark);
    }
}
