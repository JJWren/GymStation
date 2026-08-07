using System.Security.Claims;
using GymStation.Infrastructure.Ranks;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Admin;

/// <summary>Custom rank-ladder management (#139) — the finance-row idiom, with
/// every rejection reason carried back as the standard failed banner.</summary>
public static class RankSystemEndpoints
{
    public static IEndpointRouteBuilder MapRankSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/actions")
            .RequireAuthorization("Cap:ManageRanks")
            .ValidateAntiforgery();

        static async Task<IResult> Run(Func<Task> action)
        {
            try
            {
                await action();
                return Results.Redirect("/admin/ranks/ladders");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/admin/ranks/ladders?failed=1");
            }
        }

        group.MapPost("/add-rank-system", ([FromForm] string name, RankService ranks)
            => Run(() => ranks.CreateSystemAsync(name)));

        group.MapPost("/rename-rank-system", ([FromForm] Guid systemId, [FromForm] string name, RankService ranks)
            => Run(() => ranks.RenameSystemAsync(systemId, name)));

        group.MapPost("/rank-system-archived", ([FromForm] Guid systemId, [FromForm] bool archived, RankService ranks)
            => Run(() => ranks.SetSystemArchivedAsync(systemId, archived)));

        group.MapPost("/add-rank", ([FromForm] Guid systemId, [FromForm] string name, [FromForm] string bandColorHex, [FromForm] string barColorHex, [FromForm] int maxStripes, RankService ranks)
            => Run(() => ranks.AddRankAsync(systemId, name, bandColorHex, barColorHex, maxStripes)));

        group.MapPost("/update-rank", ([FromForm] Guid rankId, [FromForm] string name, [FromForm] string bandColorHex, [FromForm] string barColorHex, [FromForm] int maxStripes, RankService ranks)
            => Run(() => ranks.UpdateRankAsync(rankId, name, bandColorHex, barColorHex, maxStripes)));

        group.MapPost("/rank-move", ([FromForm] Guid rankId, [FromForm] int direction, RankService ranks)
            => Run(() => ranks.MoveRankAsync(rankId, direction)));

        group.MapPost("/remove-rank", ([FromForm] Guid rankId, RankService ranks)
            => Run(() => ranks.RemoveRankAsync(rankId)));

        group.MapPost("/rank-retired", ([FromForm] Guid rankId, [FromForm] bool retired, RankService ranks)
            => Run(() => ranks.SetRankRetiredAsync(rankId, retired)));

        // Award soft delete (#220): lives on the person page, so failure and
        // success both land back there. The deleting STAFF person is resolved
        // from the signed-in user — never a form field.
        group.MapPost("/delete-award", async (
            [FromForm] Guid awardId, [FromForm] Guid personId,
            ClaimsPrincipal user,
            GymStation.Infrastructure.GymStationDbContext db,
            RankService ranks) =>
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? deletedBy = null;
            if (Guid.TryParse(raw, out var userId))
            {
                deletedBy = (await db.Persons.SingleOrDefaultAsync(p => p.UserId == userId && !p.Archived))?.Id;
            }

            try
            {
                await ranks.DeleteAwardAsync(awardId, deletedBy);
                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        // Empty select value = clear the mapping; Guid binding would 400 on "".
        // Anything non-empty must parse — a garbled id refuses rather than clears.
        group.MapPost("/rank-system-program", ([FromForm] Guid systemId, [FromForm] string? programId, RankService ranks)
            => Run(() =>
            {
                if (string.IsNullOrEmpty(programId))
                {
                    return ranks.SetSystemProgramAsync(systemId, null);
                }

                return Guid.TryParse(programId, out var id)
                    ? ranks.SetSystemProgramAsync(systemId, id)
                    : throw new InvalidOperationException("Program id malformed.");
            }));

        // Staff override of a person's primary discipline (#215) — same
        // empty-clears / garbage-refuses contract as the mapping above.
        group.MapPost("/set-primary-discipline", async ([FromForm] Guid personId, [FromForm] string? systemId, RankService ranks) =>
        {
            try
            {
                if (string.IsNullOrEmpty(systemId))
                {
                    await ranks.SetPrimaryRankSystemAsync(personId, null);
                }
                else if (Guid.TryParse(systemId, out var id))
                {
                    await ranks.SetPrimaryRankSystemAsync(personId, id);
                }
                else
                {
                    throw new InvalidOperationException("Ladder id malformed.");
                }

                return Results.Redirect($"/admin/people/{personId}");
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }
        });

        return app;
    }
}
