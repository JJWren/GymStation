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

        // Guardians belong wherever their wards train — even with no roster record
        // of their own (the Sarah-and-Tom case). Every row's GymId must agree with
        // the ward Person's: malformed cross-tenant data grants nothing (#89).
        var viaChildren = db.FamilyGuardians.IgnoreQueryFilters()
            .Where(g => g.GuardianUserId == userId)
            .Join(db.FamilyMembers.IgnoreQueryFilters().Where(m => m.IsWard),
                g => g.FamilyId, m => m.FamilyId, (g, m) => new { g.GymId, Member = m })
            .Join(db.Persons.IgnoreQueryFilters().Where(p => !p.Archived),
                x => x.Member.PersonId, p => p.Id, (x, p) => new { x.GymId, MemberGymId = x.Member.GymId, ChildGymId = p.GymId })
            .Where(x => x.GymId == x.ChildGymId && x.MemberGymId == x.ChildGymId)
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
            || await db.FamilyGuardians.IgnoreQueryFilters()
                .Where(g => g.GuardianUserId == userId && g.GymId == gymId)
                .Join(db.FamilyMembers.IgnoreQueryFilters().Where(m => m.IsWard && m.GymId == gymId),
                    g => g.FamilyId, m => m.FamilyId, (g, m) => m)
                .Join(db.Persons.IgnoreQueryFilters(), m => m.PersonId, p => p.Id, (_, p) => p)
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
