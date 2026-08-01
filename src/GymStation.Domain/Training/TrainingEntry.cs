using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Training;

public enum TrainingEntryKind
{
    /// <summary>"What did you learn?" notes, usually attached to an attended session.</summary>
    LessonNotes = 0,

    /// <summary>Roll/sparring diary with optional partner tags.</summary>
    RollLog = 1,

    /// <summary>Training outside the gym (drop-ins elsewhere, home drilling). Feeds the
    /// self-reported hours tier only — never owner statistics.</summary>
    SelfReported = 2,
}

/// <summary>
/// A member's private diary entry. STRICTLY private to its author: never visible to
/// Instructors, Admins, or Owners — every read path scopes to the requesting user's
/// own Person, and no staff surface queries this table.
/// </summary>
public class TrainingEntry : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    /// <summary>The author's Person in this gym. The only person who can ever read it.</summary>
    public Guid PersonId { get; set; }

    public TrainingEntryKind Kind { get; set; }
    public DateOnly EntryDate { get; set; }

    /// <summary>Optional link to the attended ClassSession this entry reflects on.</summary>
    public Guid? SessionId { get; set; }

    public string? Notes { get; set; }

    /// <summary>Minutes trained, for SelfReported entries.</summary>
    public int? SelfReportedMinutes { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<TrainingRoll> Rolls { get; set; } = [];
}

/// <summary>
/// One sparring line inside an entry. A partner tag references a roster Person but lives
/// only inside the author's private entry — the tagged partner never sees it.
/// </summary>
public class TrainingRoll : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public Guid TrainingEntryId { get; set; }

    public Guid? PartnerPersonId { get; set; }

    /// <summary>Display label captured at write time (roster name or free text like "visitor").</summary>
    public required string PartnerLabel { get; set; }

    /// <summary>"2x5min · 1 sub for, 0 against" — free form.</summary>
    public string? Summary { get; set; }
}
