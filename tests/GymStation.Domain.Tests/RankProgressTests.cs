using GymStation.Domain.People;
using GymStation.Domain.Ranks;

namespace GymStation.Domain.Tests;

public class RankProgressTests
{
    private static readonly Rank Blue = new() { Id = Guid.NewGuid(), Name = "Blue", Order = 2, BandColorHex = "#2456A6", BarColorHex = "#17181A" };
    private static readonly Rank Purple = new() { Id = Guid.NewGuid(), Name = "Purple", Order = 3, BandColorHex = "#5C3D93", BarColorHex = "#17181A" };

    private static RankAward Award(Rank rank, int stripes, DateOnly on, bool selfReported = false) => new()
    {
        Id = Guid.NewGuid(),
        RankId = rank.Id,
        Rank = rank,
        Stripes = stripes,
        AwardedOn = on,
        SelfReported = selfReported,
        RecordedUtc = new DateTimeOffset(on.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
    };

    [Fact]
    public void Current_IsNull_WithNoAwards()
    {
        Assert.Null(RankProgress.Current([]));
    }

    [Fact]
    public void StripesDoNotResetTheBeltClock()
    {
        var awards = new[]
        {
            Award(Purple, 0, new DateOnly(2024, 12, 2)),
            Award(Purple, 1, new DateOnly(2025, 8, 10)),
            Award(Purple, 2, new DateOnly(2026, 6, 14)),
        };

        var current = RankProgress.Current(awards)!;

        Assert.Equal(Purple.Id, current.Rank.Id);
        Assert.Equal(2, current.Stripes);
        Assert.Equal(new DateOnly(2024, 12, 2), current.AtRankSince);
    }

    [Fact]
    public void ANewBelt_ResetsTheClock()
    {
        var awards = new[]
        {
            Award(Blue, 0, new DateOnly(2021, 3, 21), selfReported: true),
            Award(Blue, 3, new DateOnly(2024, 6, 1)),
            Award(Purple, 0, new DateOnly(2024, 12, 2)),
        };

        var current = RankProgress.Current(awards)!;

        Assert.Equal(Purple.Id, current.Rank.Id);
        Assert.Equal(new DateOnly(2024, 12, 2), current.AtRankSince);
    }

    [Fact]
    public void SelfReportedHistory_CountsTowardDerivation()
    {
        var awards = new[] { Award(Blue, 0, new DateOnly(2021, 3, 21), selfReported: true) };

        var current = RankProgress.Current(awards)!;

        Assert.Equal(Blue.Id, current.Rank.Id);
        Assert.Equal(new DateOnly(2021, 3, 21), current.AtRankSince);
    }

    [Fact]
    public void SameDayBeltThenStripe_UsesRecordedOrder()
    {
        var day = new DateOnly(2026, 7, 1);
        var belt = Award(Purple, 0, day);
        var stripe = Award(Purple, 1, day);
        stripe.RecordedUtc = belt.RecordedUtc.AddMinutes(5);

        var current = RankProgress.Current([stripe, belt])!;

        Assert.Equal(1, current.Stripes);
        Assert.Equal(day, current.AtRankSince);
    }

    [Theory]
    [InlineData(10, "new")]
    [InlineData(45, "1m")]
    [InlineData(458, "1y 3m")]
    [InlineData(732, "2y")]
    public void FormatDuration_ReadsLikeTheDesign(int totalDays, string expected)
    {
        Assert.Equal(expected, RankProgress.FormatDuration(TimeSpan.FromDays(totalDays)));
    }
}

public class IbjjfAgeGroupTests
{
    private static readonly DateOnly Today = new(2026, 7, 31);

    [Theory]
    [InlineData(2023, null)]          // turns 3 — too young
    [InlineData(2022, "Mighty Mite")] // turns 4
    [InlineData(2019, "Pee Wee")]     // turns 7
    [InlineData(2015, "Junior")]      // turns 11
    [InlineData(2012, "Teen")]        // turns 14
    [InlineData(2010, "Juvenile")]    // turns 16
    [InlineData(2008, "Adult")]       // turns 18
    [InlineData(1997, "Adult")]       // turns 29
    [InlineData(1996, "Master 1")]    // turns 30
    [InlineData(1990, "Master 2")]    // turns 36
    [InlineData(1965, "Master 7")]    // turns 61
    public void DivisionsFollowBirthYear(int birthYear, string? expected)
    {
        Assert.Equal(expected, IbjjfAgeGroup.FromBirthDate(new DateOnly(birthYear, 12, 25), Today));
    }

    [Fact]
    public void NoBirthDate_NoDivision()
    {
        Assert.Null(IbjjfAgeGroup.FromBirthDate(null, Today));
    }

    [Fact]
    public void LateBirthday_StillCountsTheYearTheyTurn()
    {
        // Born Dec 1996: still 29 on 2026-07-31, but IBJJF uses the age turned that year → Master 1.
        Assert.Equal("Master 1", IbjjfAgeGroup.FromBirthDate(new DateOnly(1996, 12, 25), Today));
    }
}
