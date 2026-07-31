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

/// <summary>Satisfied when the signed-in user's Person in the ACTIVE gym holds Instructor (or staff).</summary>
public class GymInstructorRequirement : IAuthorizationRequirement;

public class GymInstructorHandler(GymStationDbContext db, ITenantContext tenant) : AuthorizationHandler<GymInstructorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GymInstructorRequirement requirement)
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

        var allowed = await db.Persons.AnyAsync(p =>
            p.UserId == userId
            && !p.Archived
            && (p.Roles.HasFlag(PersonRoles.Instructor) || p.Roles.HasFlag(PersonRoles.Admin) || p.Roles.HasFlag(PersonRoles.Owner)));

        if (allowed)
        {
            context.Succeed(requirement);
        }
    }
}
