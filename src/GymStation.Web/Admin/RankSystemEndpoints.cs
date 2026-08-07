using GymStation.Infrastructure.Ranks;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;

namespace GymStation.Web.Admin;

/// <summary>Custom rank-ladder management (#139) — the finance-row idiom, with
/// every rejection reason carried back as the standard failed banner.</summary>
public static class RankSystemEndpoints
{
    public static IEndpointRouteBuilder MapRankSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/actions")
            .RequireAuthorization("GymStaff")
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

        return app;
    }
}
