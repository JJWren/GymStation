using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Notifications;

public enum NotificationCategory
{
    SessionCancelled = 0,
    SessionChanged = 1,
    SwapRequested = 2,
    SwapAccepted = 3,
    SwapApplied = 4,
    SwapEscalated = 5,
    RankAwarded = 6,
    ChargeRaised = 7,
}

public enum DeliveryChannel
{
    Email = 0,
    // In-app needs no delivery row: the Notification itself IS the in-app inbox entry.
}

public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// Outbox record. Created transactionally with its cause; the in-app inbox reads these
/// directly, while channel deliveries (email now, push later) fan out via the dispatcher.
/// </summary>
public class Notification : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid RecipientUserId { get; set; }

    public NotificationCategory Category { get; set; }

    public required string Title { get; set; }
    public required string Body { get; set; }

    /// <summary>In-app destination, e.g. /admin/schedule.</summary>
    public string? LinkPath { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadUtc { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = [];
}

/// <summary>One channel send for one notification, processed by the background dispatcher.</summary>
public class NotificationDelivery : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid NotificationId { get; set; }

    public DeliveryChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public DateTimeOffset? AttemptedUtc { get; set; }
    public string? Error { get; set; }
}
