using GymStation.Domain.Tenancy;

namespace GymStation.Domain.Contact;

/// <summary>
/// A ContactMessage — a visitor's inquiry from the public page's contact form:
/// name, a way to reach them (email or phone, at least one), and their message.
/// Read by staff in the Gym's Messages box. NEVER a Notification (its arrival
/// raises one), and its body is plain text — markdown is for members and staff,
/// not anonymous strangers (#138).
/// </summary>
public class ContactMessage : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid GymId { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    /// <summary>Strictly-formatted address; format + best-effort MX checked on the way in.</summary>
    public string? Email { get; set; }

    /// <summary>Digits only — the pretty "(###) ###-####" is a render concern.</summary>
    public string? Phone { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadUtc { get; set; }
}
