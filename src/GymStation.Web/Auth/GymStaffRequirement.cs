using System.Security.Claims;
using GymStation.Domain.People;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Auth;

/// <summary>Satisfied when the signed-in user's Person in the ACTIVE gym holds Admin or Owner.</summary>
public class GymStaffRequirement : IAuthorizationRequirement;

public class GymStaffHandler(GymStationDbContext db, ITenantContext tenant) : AuthorizationHandler<GymStaffRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GymStaffRequirement requirement)
    {
        if (tenant.CurrentGymId is null)
        {
            return;
        }

        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId))
        {
            return;
        }

        // Tenant query filter scopes this to the active gym automatically.
        var isStaff = await db.Persons.AnyAsync(p =>
            p.UserId == userId
            && !p.Archived
            && (p.Roles.HasFlag(PersonRoles.Admin) || p.Roles.HasFlag(PersonRoles.Owner)));

        if (isStaff)
        {
            context.Succeed(requirement);
        }
    }
}
