namespace GymStation.Domain.Tenancy;

/// <summary>The tenant: one academy at one location. Not itself tenant-filtered.</summary>
public class Gym
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>URL segment for public pages and tenant addressing (gymstation/{slug}). Unique.</summary>
    public required string Slug { get; set; }

    /// <summary>IANA time zone id; all session times render in gym time.</summary>
    public required string TimeZoneId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public GymSettings Settings { get; set; } = null!;
}
