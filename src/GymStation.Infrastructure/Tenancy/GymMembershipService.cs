using GymStation.Domain.People;
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
        var direct = db.Persons.IgnoreQueryFilters()
            .Where(p => p.UserId == userId && !p.Archived)
            .Select(p => p.GymId);

        // Guardians belong wherever their linked children train — even with no
        // roster record of their own (the Sarah-and-Tom case). The link's own GymId
        // must agree with the child's: a malformed cross-tenant link grants nothing.
        var viaChildren = db.GuardianLinks.IgnoreQueryFilters()
            .Where(l => l.GuardianUserId == userId)
            .Join(db.Persons.IgnoreQueryFilters().Where(p => !p.Archived),
                l => l.ChildPersonId, p => p.Id, (l, p) => new { l.GymId, ChildGymId = p.GymId })
            .Where(x => x.GymId == x.ChildGymId)
            .Select(x => x.ChildGymId);

        return await direct.Concat(viaChildren)
            .Distinct()
            .Join(db.Gyms, gymId => gymId, g => g.Id, (_, g) => new GymMembership(g.Id, g.Name, g.Slug))
            .ToListAsync(ct);
    }

    public async Task<bool> IsUserInGymAsync(Guid userId, Guid gymId, CancellationToken ct = default)
    {
        return await db.Persons.IgnoreQueryFilters()
                .AnyAsync(p => p.UserId == userId && p.GymId == gymId && !p.Archived, ct)
            || await db.GuardianLinks.IgnoreQueryFilters()
                .Where(l => l.GuardianUserId == userId && l.GymId == gymId)
                .Join(db.Persons.IgnoreQueryFilters(), l => l.ChildPersonId, p => p.Id, (_, p) => p)
                .AnyAsync(p => p.GymId == gymId && !p.Archived, ct);
    }

    /// <summary>
    /// Post-sign-in landing for a user in a gym. Staff run the gym from the admin
    /// Today screen; instructors land on /teach (their sessions + covers); members
    /// and guardian-only accounts live on the member schedule.
    /// </summary>
    public async Task<string> LandingPathAsync(Guid userId, Guid gymId, CancellationToken ct = default)
    {
        var roles = await db.Persons.IgnoreQueryFilters()
            .Where(p => p.UserId == userId && p.GymId == gymId && !p.Archived)
            .Select(p => (PersonRoles?)p.Roles)
            .FirstOrDefaultAsync(ct);

        if (roles is { } r)
        {
            if (r.HasFlag(PersonRoles.Admin) || r.HasFlag(PersonRoles.Owner))
            {
                return "/";
            }

            if (r.HasFlag(PersonRoles.Instructor))
            {
                return "/teach";
            }
        }

        return "/schedule";
    }
}
