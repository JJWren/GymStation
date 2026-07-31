using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using GymStation.Domain.Scheduling;
using GymStation.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Scheduling;

public class SubstitutionService(GymStationDbContext db, NotificationService notifications)
{
    public async Task<SubstitutionRequest> RequestAsync(
        Guid sessionId, Guid requestedByPersonId, Guid? proposedSubPersonId, string? note, CancellationToken ct = default)
    {
        var session = await db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in the active gym.");

        if (session.Status == SessionStatus.Cancelled)
        {
            throw new InvalidOperationException("Cannot request cover for a cancelled session.");
        }

        // Only the session's assigned instructor (or gym staff acting on their behalf)
        // can put a session up for cover.
        if (session.InstructorPersonId != requestedByPersonId)
        {
            var requester = await db.Persons.SingleOrDefaultAsync(p => p.Id == requestedByPersonId && !p.Archived, ct)
                ?? throw new InvalidOperationException("Requester not found in the active gym.");

            if (!requester.HasRole(PersonRoles.Admin) && !requester.HasRole(PersonRoles.Owner))
            {
                throw new InvalidOperationException("Only the session's instructor or gym staff can request cover.");
            }
        }

        var settings = await db.GymSettings.SingleAsync(ct);
        if (proposedSubPersonId is null && !settings.OpenClaimsEnabled)
        {
            throw new InvalidOperationException("Open requests are disabled for this gym — name a substitute.");
        }

        var request = new SubstitutionRequest
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            RequestedByPersonId = requestedByPersonId,
            ProposedSubPersonId = proposedSubPersonId,
            Note = note,
        };
        db.SubstitutionRequests.Add(request);

        var when = $"{session.Date:ddd dd MMM} {session.StartTime:HH\\:mm}";
        var requesterUserIds = await notifications.UserIdsForPersonsAsync([requestedByPersonId], ct);
        var recipients = proposedSubPersonId is { } named
            ? await notifications.UserIdsForPersonsAsync([named], ct)
            : (await notifications.InstructorUserIdsAsync(ct)).Except(requesterUserIds).ToList();

        notifications.Notify(
            recipients,
            NotificationCategory.SwapRequested,
            $"Cover needed: {session.Name} · {when}",
            proposedSubPersonId is null
                ? $"An open cover request is up for {session.Name} on {when}. First instructor to claim it teaches."
                : $"You've been proposed to cover {session.Name} on {when}. Accept from your substitutions page.",
            "/instructor/swaps");

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<SubstitutionRequest> AcceptAsync(Guid requestId, Guid acceptingPersonId, CancellationToken ct = default)
    {
        var request = await db.SubstitutionRequests.Include(r => r.Session).SingleOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Substitution request not found in the active gym.");

        var settings = await db.GymSettings.SingleAsync(ct);
        var result = SubstitutionMachine.Accept(request, settings.SubstitutionMode, acceptingPersonId, settings.OpenClaimsEnabled);

        var when = $"{request.Session.Date:ddd dd MMM} {request.Session.StartTime:HH\\:mm}";

        if (result == SubstitutionStatus.Applied)
        {
            ApplyToSession(request);
            notifications.Notify(
                [.. await notifications.StaffUserIdsAsync(ct), .. await notifications.UserIdsForPersonsAsync([request.RequestedByPersonId], ct)],
                NotificationCategory.SwapApplied,
                $"Covered: {request.Session.Name} · {when}",
                $"{request.Session.Name} on {when} is covered. The schedule has been updated automatically (auto-apply gym).",
                "/admin/schedule");
        }
        else
        {
            notifications.Notify(
                await notifications.StaffUserIdsAsync(ct),
                NotificationCategory.SwapAccepted,
                $"Approval needed: {request.Session.Name} · {when}",
                $"A substitute accepted for {request.Session.Name} on {when}. This gym requires admin approval before the schedule updates.",
                "/admin/schedule");
        }

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task ApproveAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await db.SubstitutionRequests.Include(r => r.Session).SingleOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Substitution request not found in the active gym.");

        SubstitutionMachine.Approve(request);
        ApplyToSession(request);

        var when = $"{request.Session.Date:ddd dd MMM} {request.Session.StartTime:HH\\:mm}";
        notifications.Notify(
            await notifications.UserIdsForPersonsAsync(
                new[] { request.RequestedByPersonId, request.AcceptedByPersonId!.Value }, ct),
            NotificationCategory.SwapApplied,
            $"Approved: {request.Session.Name} · {when}",
            $"The substitution for {request.Session.Name} on {when} was approved — the schedule now shows the covering instructor.",
            "/instructor/swaps");

        await db.SaveChangesAsync(ct);
    }

    public async Task DeclineAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await db.SubstitutionRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Substitution request not found in the active gym.");

        SubstitutionMachine.Decline(request);
        await db.SaveChangesAsync(ct);
    }

    public async Task WithdrawAsync(Guid requestId, Guid byPersonId, CancellationToken ct = default)
    {
        var request = await db.SubstitutionRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Substitution request not found in the active gym.");

        SubstitutionMachine.Withdraw(request, byPersonId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Escalate unfilled requests whose session starts within 24h (gym-local time).
    /// Called by the background worker under each gym's tenant context; also directly testable.
    /// Returns the number escalated.
    /// </summary>
    public async Task<int> EscalateDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var gym = await db.Gyms.SingleAsync(g => g.Id == db.CurrentGymId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(gym.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone);

        var open = await db.SubstitutionRequests
            .Include(r => r.Session)
            .Where(r => r.Status == SubstitutionStatus.Requested && r.EscalatedUtc == null)
            .ToListAsync(ct);

        var escalated = 0;
        foreach (var request in open)
        {
            var sessionLocal = request.Session.Date.ToDateTime(request.Session.StartTime);
            if (sessionLocal - localNow.DateTime > TimeSpan.FromHours(24) || sessionLocal < localNow.DateTime)
            {
                continue;
            }

            request.EscalatedUtc = nowUtc;
            escalated++;

            var when = $"{request.Session.Date:ddd dd MMM} {request.Session.StartTime:HH\\:mm}";
            notifications.Notify(
                await notifications.StaffUserIdsAsync(ct),
                NotificationCategory.SwapEscalated,
                $"UNFILLED: {request.Session.Name} · {when}",
                $"{request.Session.Name} on {when} still has no cover and starts within 24 hours. Assign someone or cancel the session.",
                "/admin/schedule");
        }

        if (escalated > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return escalated;
    }

    private static void ApplyToSession(SubstitutionRequest request)
    {
        request.Session.InstructorPersonId = request.AcceptedByPersonId;
    }
}
