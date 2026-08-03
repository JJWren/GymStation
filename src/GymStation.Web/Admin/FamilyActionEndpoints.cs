using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.People;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GymStation.Web.Admin;

/// <summary>
/// Staff structure ops on families (#89): create/repair membership and guardianship.
/// Every rule lives in FamilyService — these endpoints just carry the forms, acting
/// as FamilyActor.Staff (structure-only; acting-for-wards has no admin path at all).
/// </summary>
public static class FamilyActionEndpoints
{
    public static IEndpointRouteBuilder MapFamilyActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/family-actions")
            .RequireAuthorization("GymStaff")
            .ValidateAntiforgery();

        group.MapPost("/create", async ([FromForm] string name, FamilyService families) =>
        {
            try
            {
                var family = await families.CreateFamilyAsync(FamilyActor.Staff, name);
                return Results.Redirect($"/admin/families/{family.Id}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/families?failed=1");
            }
        });

        group.MapPost("/rename", async ([FromForm] Guid familyId, [FromForm] string name, FamilyService families) =>
            await RunAsync(familyId, () => families.RenameFamilyAsync(FamilyActor.Staff, familyId, name)));

        group.MapPost("/add-member", async (
            [FromForm] Guid familyId, [FromForm] Guid personId, FamilyService families, [FromForm] bool isWard = false) =>
            await RunAsync(familyId, () => families.AddMemberAsync(FamilyActor.Staff, familyId, personId, isWard)));

        group.MapPost("/set-ward", async (
            [FromForm] Guid familyId, [FromForm] Guid personId, [FromForm] bool isWard, FamilyService families) =>
            await RunAsync(familyId, () => families.SetWardAsync(FamilyActor.Staff, familyId, personId, isWard)));

        group.MapPost("/remove-member", async ([FromForm] Guid familyId, [FromForm] Guid personId, FamilyService families) =>
            await RunAsync(familyId, () => families.RemoveMemberAsync(FamilyActor.Staff, familyId, personId)));

        // The guardian arrives as an email: logins are global, so staff attach an
        // existing account — there's no invite flow here (graduation, #92, owns invites).
        group.MapPost("/add-guardian", async (
            [FromForm] Guid familyId, [FromForm] string email,
            UserManager<AppUser> users, FamilyService families,
            [FromForm] bool actForWards = false, [FromForm] bool manageGuardians = false,
            [FromForm] bool manageMembers = false, [FromForm] bool viewBilling = false) =>
        {
            var user = await users.FindByEmailAsync(email.Trim());
            if (user is null)
            {
                return Results.Redirect($"/admin/families/{familyId}?failed=2");
            }

            return await RunAsync(familyId, () => families.AddGuardianAsync(
                FamilyActor.Staff, familyId, user.Id, actForWards, manageGuardians, manageMembers, viewBilling));
        });

        group.MapPost("/set-flags", async (
            [FromForm] Guid familyId, [FromForm] Guid guardianId, FamilyService families,
            [FromForm] bool actForWards = false, [FromForm] bool manageGuardians = false,
            [FromForm] bool manageMembers = false, [FromForm] bool viewBilling = false) =>
            await RunAsync(familyId, () => families.SetGuardianFlagsAsync(
                FamilyActor.Staff, familyId, guardianId, actForWards, manageGuardians, manageMembers, viewBilling)));

        group.MapPost("/remove-guardian", async ([FromForm] Guid familyId, [FromForm] Guid guardianId, FamilyService families) =>
            await RunAsync(familyId, () => families.RemoveGuardianAsync(FamilyActor.Staff, familyId, guardianId)));

        group.MapPost("/transfer-primary", async ([FromForm] Guid familyId, [FromForm] Guid guardianId, FamilyService families) =>
            await RunAsync(familyId, () => families.TransferPrimaryAsync(FamilyActor.Staff, familyId, guardianId)));

        return app;
    }

    private static async Task<IResult> RunAsync(Guid familyId, Func<Task> action)
    {
        try
        {
            await action();
            return Results.Redirect($"/admin/families/{familyId}");
        }
        catch (InvalidOperationException)
        {
            return Results.Redirect($"/admin/families/{familyId}?failed=1");
        }
    }
}
