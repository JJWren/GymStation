using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Scheduling;

/// <summary>
/// Pure state transitions for substitution requests. The gym's SubstitutionMode decides
/// whether acceptance lands on Applied (auto-apply) or PendingApproval (admin-gate).
/// Callers persist the request and perform side effects (session update, notifications).
/// </summary>
public static class SubstitutionMachine
{
    /// <summary>Sub accepts a named request, or any instructor claims an open one.</summary>
    public static SubstitutionStatus Accept(SubstitutionRequest request, SubstitutionMode mode, Guid acceptingPersonId, bool openClaimsEnabled)
    {
        if (request.Status != SubstitutionStatus.Requested)
        {
            throw new InvalidOperationException($"Only a Requested substitution can be accepted (was {request.Status}).");
        }

        if (request.ProposedSubPersonId is { } named)
        {
            if (named != acceptingPersonId)
            {
                throw new InvalidOperationException("This request names a different substitute.");
            }
        }
        else if (!openClaimsEnabled)
        {
            throw new InvalidOperationException("Open claims are disabled for this gym.");
        }

        if (acceptingPersonId == request.RequestedByPersonId)
        {
            throw new InvalidOperationException("The requesting instructor cannot cover their own request.");
        }

        request.AcceptedByPersonId = acceptingPersonId;
        request.AcceptedUtc = DateTimeOffset.UtcNow;
        request.Status = mode == SubstitutionMode.AdminGate
            ? SubstitutionStatus.PendingApproval
            : SubstitutionStatus.Applied;

        if (request.Status == SubstitutionStatus.Applied)
        {
            request.ResolvedUtc = request.AcceptedUtc;
        }

        return request.Status;
    }

    /// <summary>Admin approval in admin-gated gyms.</summary>
    public static void Approve(SubstitutionRequest request)
    {
        if (request.Status != SubstitutionStatus.PendingApproval)
        {
            throw new InvalidOperationException($"Only a PendingApproval substitution can be approved (was {request.Status}).");
        }

        request.Status = SubstitutionStatus.Applied;
        request.ResolvedUtc = DateTimeOffset.UtcNow;
    }

    public static void Decline(SubstitutionRequest request)
    {
        if (request.Status is not (SubstitutionStatus.Requested or SubstitutionStatus.PendingApproval))
        {
            throw new InvalidOperationException($"Cannot decline a substitution in state {request.Status}.");
        }

        request.Status = SubstitutionStatus.Declined;
        request.ResolvedUtc = DateTimeOffset.UtcNow;
    }

    public static void Withdraw(SubstitutionRequest request, Guid byPersonId)
    {
        if (byPersonId != request.RequestedByPersonId)
        {
            throw new InvalidOperationException("Only the requesting instructor can withdraw.");
        }

        if (request.Status is not (SubstitutionStatus.Requested or SubstitutionStatus.PendingApproval))
        {
            throw new InvalidOperationException($"Cannot withdraw a substitution in state {request.Status}.");
        }

        request.Status = SubstitutionStatus.Withdrawn;
        request.ResolvedUtc = DateTimeOffset.UtcNow;
    }
}
