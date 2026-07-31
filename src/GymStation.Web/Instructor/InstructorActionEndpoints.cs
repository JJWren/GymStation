using System.Security.Claims;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Scheduling;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Instructor;

/// <summary>Instructor-side substitution actions (request cover, claim, accept, withdraw).</summary>
public static class InstructorActionEndpoints
{
    public static IEndpointRouteBuilder MapInstructorActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/instructor/actions")
            .RequireAuthorization("GymInstructor")
            .ValidateAntiforgery();

        group.MapPost("/request-sub", async (
            [FromForm] Guid sessionId,
            [FromForm] Guid? proposedSubPersonId,
            [FromForm] string? note,
            ClaimsPrincipal user,
            GymStationDbContext db,
            SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            await subs.RequestAsync(sessionId, personId.Value, proposedSubPersonId, note);
            return Results.Redirect("/instructor/swaps");
        });

        group.MapPost("/accept-sub", async ([FromForm] Guid requestId, ClaimsPrincipal user, GymStationDbContext db, SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            try
            {
                await subs.AcceptAsync(requestId, personId.Value);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            return Results.Redirect("/instructor/swaps");
        });

        group.MapPost("/withdraw-sub", async ([FromForm] Guid requestId, ClaimsPrincipal user, GymStationDbContext db, SubstitutionService subs) =>
        {
            var personId = await PersonIdAsync(db, user);
            if (personId is null)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            try
            {
                await subs.WithdrawAsync(requestId, personId.Value);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/instructor/swaps?failed=1");
            }

            return Results.Redirect("/instructor/swaps");
        });

        return app;
    }

    private static async Task<Guid?> PersonIdAsync(GymStationDbContext db, ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId))
        {
            return null;
        }

        return (await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived))?.Id;
    }
}
