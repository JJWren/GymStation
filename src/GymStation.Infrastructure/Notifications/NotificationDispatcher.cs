using GymStation.Domain.Notifications;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Notifications;

/// <summary>
/// Background outbox processor: fans pending channel deliveries out through their
/// adapters. Runs platform-wide (crosses tenants deliberately), but writes each
/// gym's rows under that gym's tenant context to satisfy the write guard.
/// </summary>
public class NotificationDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch pass failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task DispatchPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymStationDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailDeliverer>();

        var pending = await db.NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.Status == DeliveryStatus.Pending && d.Channel == DeliveryChannel.Email)
            .OrderBy(d => d.Id)
            .Take(25)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return;
        }

        var notificationIds = pending.Select(d => d.NotificationId).ToList();
        var notifications = await db.Notifications.IgnoreQueryFilters()
            .Where(n => notificationIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, ct);

        var recipientIds = notifications.Values.Select(n => n.RecipientUserId).Distinct().ToList();
        var emails = await db.Users
            .Where(u => recipientIds.Contains(u.Id) && u.Email != null)
            .ToDictionaryAsync(u => u.Id, u => u.Email!, ct);

        foreach (var byGym in pending.GroupBy(d => d.GymId))
        {
            tenant.SetGym(byGym.Key);

            foreach (var delivery in byGym)
            {
                delivery.AttemptedUtc = DateTimeOffset.UtcNow;

                if (!notifications.TryGetValue(delivery.NotificationId, out var notification)
                    || !emails.TryGetValue(notification.RecipientUserId, out var to))
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.Error = "Recipient has no email address.";
                    continue;
                }

                try
                {
                    await email.SendAsync(to, notification.Title, notification.Body, ct);
                    delivery.Status = DeliveryStatus.Sent;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.Error = ex.Message.Length > 480 ? ex.Message[..480] : ex.Message;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        tenant.Clear();
    }
}
