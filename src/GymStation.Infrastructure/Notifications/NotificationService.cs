using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Notifications;

/// <summary>
/// Creates outbox rows. Deliberately does NOT SaveChanges: notifications are added to the
/// caller's unit of work so they commit transactionally with their cause. The in-app inbox
/// reads Notification rows directly; email fans out via NotificationDispatcher.
/// </summary>
public class NotificationService(GymStationDbContext db)
{
    public void Notify(
        IEnumerable<Guid> recipientUserIds,
        NotificationCategory category,
        string title,
        string body,
        string? linkPath = null,
        bool email = true)
    {
        foreach (var userId in recipientUserIds.Distinct())
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                RecipientUserId = userId,
                Category = category,
                Title = title,
                Body = body,
                LinkPath = linkPath,
            };

            if (email)
            {
                notification.Deliveries.Add(new NotificationDelivery
                {
                    Id = Guid.NewGuid(),
                    NotificationId = notification.Id,
                    Channel = DeliveryChannel.Email,
                });
            }

            db.Notifications.Add(notification);
        }
    }

    /// <summary>User ids of the active gym's Owners/Admins that can sign in.</summary>
    public async Task<List<Guid>> StaffUserIdsAsync(CancellationToken ct = default)
    {
        return await db.Persons
            .Where(p => !p.Archived && p.UserId != null
                && (p.Roles.HasFlag(PersonRoles.Admin) || p.Roles.HasFlag(PersonRoles.Owner)))
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>User ids of the active gym's instructors that can sign in.</summary>
    public async Task<List<Guid>> InstructorUserIdsAsync(CancellationToken ct = default)
    {
        return await db.Persons
            .Where(p => !p.Archived && p.UserId != null && p.Roles.HasFlag(PersonRoles.Instructor))
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<List<Guid>> UserIdsForPersonsAsync(IEnumerable<Guid> personIds, CancellationToken ct = default)
    {
        return await db.Persons
            .Where(p => personIds.Contains(p.Id) && p.UserId != null)
            .Select(p => p.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }
}
