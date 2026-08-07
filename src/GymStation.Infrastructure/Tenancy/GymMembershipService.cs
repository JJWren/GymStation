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

    /// <summary>The landing views a user's roles open in a gym (#218): "admin"
    /// for staff, "teach" for instructors, and "member" always (every signed-in
    /// shape has the member shell — guardians included).</summary>
    public async Task<IReadOnlyList<string>> AvailableViewsAsync(Guid userId, Guid gymId, CancellationToken ct = default)
    {
        var roles = await db.Persons.IgnoreQueryFilters()
            .Where(p => p.UserId == userId && p.GymId == gymId && !p.Archived)
            .Select(p => (PersonRoles?)p.Roles)
            .FirstOrDefaultAsync(ct);

        var views = new List<string>();
        if (roles is { } r)
        {
            if (r.HasFlag(PersonRoles.Admin) || r.HasFlag(PersonRoles.Owner))
            {
                views.Add("admin");
            }

            if (r.HasFlag(PersonRoles.Instructor))
            {
                views.Add("teach");
            }
        }

        views.Add("member");
        return views;
    }

    public static string ViewPath(string view) => view switch
    {
        "admin" => "/",
        "teach" => "/teach",
        _ => "/schedule",
    };

    /// <summary>
    /// Post-sign-in landing for a user in a gym. Single-view users go straight
    /// in; multi-view users follow their saved default, and are prompted ONCE
    /// (/choose-view) when they haven't picked one yet (#218).
    /// </summary>
    public async Task<string> LandingPathAsync(Guid userId, Guid gymId, CancellationToken ct = default)
    {
        var views = await AvailableViewsAsync(userId, gymId, ct);
        if (views.Count == 1)
        {
            return ViewPath(views[0]);
        }

        var preferred = await db.Users.Where(u => u.Id == userId)
            .Select(u => u.PreferredLandingView)
            .FirstOrDefaultAsync(ct);

        // A stale preference (role since revoked) falls through to the prompt
        // rather than bouncing off a page the policies will refuse.
        return preferred is not null && views.Contains(preferred)
            ? ViewPath(preferred)
            : "/choose-view";
    }
}
