using Microsoft.AspNetCore.Identity;

namespace GymStation.Infrastructure.Identity;

/// <summary>Global authentication identity: one login per human, valid across all gyms (ADR 0002).</summary>
public class AppUser : IdentityUser<Guid>
{
}
