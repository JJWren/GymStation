using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Marketing;

/// <summary>
/// A SuccessStory — a testimonial the Gym publishes on its public page: the
/// story and who it's by. The section carries ONE shared image (on GymSettings),
/// not one per card, by decision U14.
/// </summary>
public class SuccessStory : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    /// <summary>Markdown; renders through the one audited pipeline (#133).</summary>
    public required string Body { get; set; }

    /// <summary>Who said it — "Sam O., blue belt".</summary>
    public string? AttributedTo { get; set; }

    public int SortOrder { get; set; }
    public bool Archived { get; set; }
}
