namespace GymStation.Domain.People;

/// <summary>
/// IBJJF age division, derived from birth year — the age a competitor turns in the
/// current calendar year decides the division. Always computed, never stored.
/// </summary>
public static class IbjjfAgeGroup
{
    public static string? FromBirthDate(DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth is not { } dob)
        {
            return null;
        }

        var ageThisYear = today.Year - dob.Year;

        return ageThisYear switch
        {
            < 4 => null,
            <= 6 => "Mighty Mite",
            <= 9 => "Pee Wee",
            <= 12 => "Junior",
            <= 15 => "Teen",
            <= 17 => "Juvenile",
            <= 29 => "Adult",
            <= 35 => "Master 1",
            <= 40 => "Master 2",
            <= 45 => "Master 3",
            <= 50 => "Master 4",
            <= 55 => "Master 5",
            <= 60 => "Master 6",
            _ => "Master 7",
        };
    }
}
