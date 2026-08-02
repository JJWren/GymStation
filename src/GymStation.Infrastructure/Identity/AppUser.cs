using Microsoft.AspNetCore.Identity;

namespace GymStation.Infrastructure.Identity;

/// <summary>Global authentication identity: one login per human, valid across all gyms (ADR 0002).</summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>Per-user theme choice; null follows the gym's default mode (decision 3, issue #34).</summary>
    public bool? PreferredThemeDark { get; set; }

    /// <summary>The gym this login last activated. The claims principal factory re-injects
    /// it as the active-gym claim on EVERY principal build — including the security-stamp
    /// validator's silent cookie refresh, which used to drop the login-time claim and
    /// strand signed-in users on "no active gym" (issue #76).</summary>
    public Guid? ActiveGymId { get; set; }
}
