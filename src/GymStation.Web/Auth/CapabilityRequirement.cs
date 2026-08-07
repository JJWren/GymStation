using System.Security.Claims;
using GymStation.Domain.People;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Auth;

/// <summary>
/// Satisfied when the signed-in user's Person in the ACTIVE gym is an Owner
/// (implicitly all-capable) or a staff-ish person holding a grant for the
/// capability (#217). This is THE security boundary — nav-link hiding is UX.
/// </summary>
public class CapabilityRequirement(GymCapability capability) : IAuthorizationRequirement
{
    public GymCapability Capability { get; } = capability;

    /// <summary>Policy name for a capability, e.g. "Cap:ManageRanks".</summary>
    public static string PolicyName(GymCapability capability) => $"Cap:{capability}";
}

public class CapabilityHandler(GymStationDbContext db, ITenantContext tenant) : AuthorizationHandler<CapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CapabilityRequirement requirement)
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

        // Tenant query filter scopes to the active gym. A grant row on a person
        // who is no longer staff-ish is dead weight, never access.
        var person = await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived);
        if (person is null)
        {
            return;
        }

        if (person.HasRole(PersonRoles.Owner))
        {
            context.Succeed(requirement);
            return;
        }

        var staffish = person.HasRole(PersonRoles.Admin) || person.HasRole(PersonRoles.Instructor);
        if (staffish && await db.PermissionGrants.AnyAsync(g => g.PersonId == person.Id && g.Capability == requirement.Capability))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Satisfied only by the gym's Owner(s) — grants management itself.</summary>
public class GymOwnerRequirement : IAuthorizationRequirement;

public class GymOwnerHandler(GymStationDbContext db, ITenantContext tenant) : AuthorizationHandler<GymOwnerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GymOwnerRequirement requirement)
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

        var isOwner = await db.Persons.AnyAsync(p =>
            p.UserId == userId && !p.Archived && p.Roles.HasFlag(PersonRoles.Owner));

        if (isOwner)
        {
            context.Succeed(requirement);
        }
    }
}
