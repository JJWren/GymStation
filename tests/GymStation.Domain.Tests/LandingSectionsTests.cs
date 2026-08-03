using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Tests;

public class LandingSectionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage,also-garbage")]
    public void Normalize_DegradesJunkToTheDefaultFunnel(string? stored)
        => Assert.Equal(LandingSections.Default, LandingSections.Normalize(stored));

    [Fact]
    public void Normalize_KeepsValidOrder_DropsDupes_AppendsMissing()
    {
        var order = LandingSections.Normalize("visit, VISIT, stories,nonsense,about");
        Assert.Equal(["visit", "stories", "about", "programs", "schedule", "instructors"], order);
    }

    [Fact]
    public void Move_SwapsOneStep_AndClampsAtEdges()
    {
        var moved = LandingSections.Move(null, "programs", -1);
        Assert.Equal("programs,about,schedule,instructors,stories,visit", moved);

        // Already first — clamp, not throw.
        Assert.Equal(moved, LandingSections.Move(moved, "programs", -1));

        // Unknown key and bad direction are no-ops.
        Assert.Equal(string.Join(',', LandingSections.Default), LandingSections.Move(null, "nope", 1));
        Assert.Equal(string.Join(',', LandingSections.Default), LandingSections.Move(null, "about", 3));
    }
}
