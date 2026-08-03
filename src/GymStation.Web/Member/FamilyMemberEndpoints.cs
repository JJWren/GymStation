using System.Security.Claims;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.People;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GymStation.Web.Member;

/// <summary>
/// Guardian self-service on the MY FAMILY page (#90). Same FamilyService ops the admin
/// surface uses, but acting as the CALLER — every flag check happens in the service,
/// so a guardian without the right flag gets refused no matter what they post.
/// </summary>
public static class FamilyMemberEndpoints
{
    public static IEndpointRouteBuilder MapFamilyMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/family-actions")
            .RequireAuthorization()
            .ValidateAntiforgery();

        group.MapPost("/rename", async ([FromForm] Guid familyId, [FromForm] string name, ClaimsPrincipal user, FamilyService families) =>
            await RunAsync(user, actor => families.RenameFamilyAsync(actor, familyId, name)));

        group.MapPost("/set-ward", async (
            [FromForm] Guid familyId, [FromForm] Guid personId, ClaimsPrincipal user, FamilyService families,
            [FromForm] bool isWard = false) =>
            await RunAsync(user, actor => families.SetWardAsync(actor, familyId, personId, isWard)));

        group.MapPost("/remove-member", async ([FromForm] Guid familyId, [FromForm] Guid personId, ClaimsPrincipal user, FamilyService families) =>
            await RunAsync(user, actor => families.RemoveMemberAsync(actor, familyId, personId)));

        group.MapPost("/add-guardian", async (
            [FromForm] Guid familyId, [FromForm] string email, ClaimsPrincipal user,
            UserManager<AppUser> users, FamilyService families,
            [FromForm] bool actForWards = false, [FromForm] bool manageGuardians = false,
            [FromForm] bool manageMembers = false, [FromForm] bool viewBilling = false) =>
        {
            var target = await users.FindByEmailAsync(email.Trim());
            if (target is null)
            {
                return Results.Redirect("/family?failed=2");
            }

            return await RunAsync(user, actor => families.AddGuardianAsync(
                actor, familyId, target.Id, actForWards, manageGuardians, manageMembers, viewBilling));
        });

        group.MapPost("/set-flags", async (
            [FromForm] Guid familyId, [FromForm] Guid guardianId, ClaimsPrincipal user, FamilyService families,
            [FromForm] bool actForWards = false, [FromForm] bool manageGuardians = false,
            [FromForm] bool manageMembers = false, [FromForm] bool viewBilling = false) =>
            await RunAsync(user, actor => families.SetGuardianFlagsAsync(
                actor, familyId, guardianId, actForWards, manageGuardians, manageMembers, viewBilling)));

        group.MapPost("/remove-guardian", async ([FromForm] Guid familyId, [FromForm] Guid guardianId, ClaimsPrincipal user, FamilyService families) =>
            await RunAsync(user, actor => families.RemoveGuardianAsync(actor, familyId, guardianId)));

        group.MapPost("/transfer-primary", async ([FromForm] Guid familyId, [FromForm] Guid guardianId, ClaimsPrincipal user, FamilyService families) =>
            await RunAsync(user, actor => families.TransferPrimaryAsync(actor, familyId, guardianId)));

        return app;
    }

    private static async Task<IResult> RunAsync(ClaimsPrincipal user, Func<FamilyActor, Task> action)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId))
        {
            return Results.Redirect("/family?failed=1");
        }

        try
        {
            await action(FamilyActor.User(userId));
            return Results.Redirect("/family");
        }
        catch (InvalidOperationException)
        {
            return Results.Redirect("/family?failed=1");
        }
    }
}
