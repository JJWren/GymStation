using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Tests;

public class SubstitutionMachineTests
{
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Sub = Guid.NewGuid();

    private static SubstitutionRequest Named() => new()
    {
        Id = Guid.NewGuid(),
        RequestedByPersonId = Requester,
        ProposedSubPersonId = Sub,
    };

    private static SubstitutionRequest Open() => new()
    {
        Id = Guid.NewGuid(),
        RequestedByPersonId = Requester,
    };

    [Fact]
    public void AutoApply_AcceptLandsOnApplied()
    {
        var request = Named();

        var result = SubstitutionMachine.Accept(request, SubstitutionMode.AutoApply, Sub, openClaimsEnabled: true);

        Assert.Equal(SubstitutionStatus.Applied, result);
        Assert.Equal(Sub, request.AcceptedByPersonId);
        Assert.NotNull(request.ResolvedUtc);
    }

    [Fact]
    public void AdminGate_AcceptWaitsForApproval()
    {
        var request = Named();

        var result = SubstitutionMachine.Accept(request, SubstitutionMode.AdminGate, Sub, openClaimsEnabled: true);

        Assert.Equal(SubstitutionStatus.PendingApproval, result);
        Assert.Null(request.ResolvedUtc);

        SubstitutionMachine.Approve(request);

        Assert.Equal(SubstitutionStatus.Applied, request.Status);
        Assert.NotNull(request.ResolvedUtc);
    }

    [Fact]
    public void OpenRequest_AnyInstructorCanClaim_WhenEnabled()
    {
        var request = Open();
        var claimer = Guid.NewGuid();

        SubstitutionMachine.Accept(request, SubstitutionMode.AutoApply, claimer, openClaimsEnabled: true);

        Assert.Equal(claimer, request.AcceptedByPersonId);
    }

    [Fact]
    public void OpenRequest_RejectedWhenOpenClaimsDisabled()
    {
        var request = Open();

        Assert.Throws<InvalidOperationException>(
            () => SubstitutionMachine.Accept(request, SubstitutionMode.AutoApply, Guid.NewGuid(), openClaimsEnabled: false));
    }

    [Fact]
    public void NamedRequest_OnlyTheNamedSubCanAccept()
    {
        var request = Named();

        Assert.Throws<InvalidOperationException>(
            () => SubstitutionMachine.Accept(request, SubstitutionMode.AutoApply, Guid.NewGuid(), openClaimsEnabled: true));
    }

    [Fact]
    public void RequesterCannotCoverThemself()
    {
        var request = Open();

        Assert.Throws<InvalidOperationException>(
            () => SubstitutionMachine.Accept(request, SubstitutionMode.AutoApply, Requester, openClaimsEnabled: true));
    }

    [Fact]
    public void Approve_RequiresPendingApproval()
    {
        var request = Named();

        Assert.Throws<InvalidOperationException>(() => SubstitutionMachine.Approve(request));
    }

    [Fact]
    public void Withdraw_OnlyByRequester_AndOnlyWhileOpen()
    {
        var request = Named();

        Assert.Throws<InvalidOperationException>(() => SubstitutionMachine.Withdraw(request, Sub));

        SubstitutionMachine.Withdraw(request, Requester);
        Assert.Equal(SubstitutionStatus.Withdrawn, request.Status);

        Assert.Throws<InvalidOperationException>(() => SubstitutionMachine.Withdraw(request, Requester));
    }

    [Fact]
    public void Decline_FromRequestedOrPending_Only()
    {
        var declined = Named();
        SubstitutionMachine.Decline(declined);
        Assert.Equal(SubstitutionStatus.Declined, declined.Status);

        Assert.Throws<InvalidOperationException>(() => SubstitutionMachine.Decline(declined));
    }
}
