using Microsoft.AspNetCore.Identity;

namespace GymStation.Infrastructure.Identity;

/// <summary>Global authentication identity: one login per human, valid across all gyms (ADR 0002).</summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>Per-user theme choice; null follows the gym's default mode (decision 3, issue #34).</summary>
    public bool? PreferredThemeDark { get; set; }
}
