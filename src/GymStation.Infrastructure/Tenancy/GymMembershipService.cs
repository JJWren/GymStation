using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Tenancy;

public record GymMembership(Guid GymId, string GymName, string GymSlug);

/// <summary>
/// The one sanctioned cross-tenant read: which gyms a User belongs to,
/// powering the post-login gym picker (shown only to multi-gym users).
/// </summary>
public class GymMembershipService(GymStationDbContext db)
{
    public async Task<IReadOnlyList<GymMembership>> GetGymsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.Persons.IgnoreQueryFilters()
            .Where(p => p.UserId == userId && !p.Archived)
            .Join(db.Gyms, p => p.GymId, g => g.Id, (p, g) => new GymMembership(g.Id, g.Name, g.Slug))
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<bool> IsUserInGymAsync(Guid userId, Guid gymId, CancellationToken ct = default)
    {
        return await db.Persons.IgnoreQueryFilters()
            .AnyAsync(p => p.UserId == userId && p.GymId == gymId && !p.Archived, ct);
    }
}
