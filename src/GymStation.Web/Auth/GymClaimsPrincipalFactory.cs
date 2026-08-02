using System.Security.Claims;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Web.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymStation.Web.Auth;

/// <summary>
/// Injects the active-gym claim from <see cref="AppUser.ActiveGymId"/> into every
/// principal this app builds. Identity regenerates principals outside the login flow
/// (the security-stamp validator refreshes the cookie roughly every 30 minutes), and
/// claims added only at sign-in do not survive that — the factory is the one place
/// every rebuild passes through (issue #76).
/// </summary>
public class GymClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    IOptions<IdentityOptions> options,
    IDbContextFactory<GymStationDbContext> dbFactory)
    : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.ActiveGymId is { } gymId && await IsStillInGymAsync(user.Id, gymId))
        {
            identity.AddClaim(new Claim(ActiveGymMiddleware.ActiveGymClaim, gymId.ToString()));
        }

        return identity;
    }

    // Defense against a stale column: a gym the user has since left never becomes
    // their tenant again. Query filters need a tenant to exist, so this check runs
    // unfiltered on purpose — mirroring GymMembershipService.IsUserInGymAsync,
    // including the child join: a GuardianLink only counts while its child Person
    // is in the same gym and not archived.
    private async Task<bool> IsStillInGymAsync(Guid userId, Guid gymId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Persons.IgnoreQueryFilters()
                   .AnyAsync(p => p.GymId == gymId && p.UserId == userId && !p.Archived)
               || await db.GuardianLinks.IgnoreQueryFilters()
                   .Where(l => l.GuardianUserId == userId && l.GymId == gymId)
                   .Join(db.Persons.IgnoreQueryFilters(), l => l.ChildPersonId, p => p.Id, (_, p) => p)
                   .AnyAsync(p => p.GymId == gymId && !p.Archived);
    }
}
