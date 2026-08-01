using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Events;

public enum GymEventKind
{
    Tournament = 0,
    Seminar = 1,
    Grading = 2,
    Other = 3,
}

public enum RsvpStatus
{
    Going = 0,
    Interested = 1,
}

/// <summary>An admin-published happening (tournament, seminar, grading). Not a ClassSession.</summary>
public class GymEvent : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string Title { get; set; }
    public GymEventKind Kind { get; set; }
    public DateOnly StartsOn { get; set; }
    public string? TimeInfo { get; set; }
    public string? Location { get; set; }
    public string? Details { get; set; }

    public Guid PublishedByPersonId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Going/interested — deliberately visible within the gym (team hype is the point).</summary>
public class EventRsvp : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid EventId { get; set; }
    public Guid PersonId { get; set; }
    public RsvpStatus Status { get; set; }
}
