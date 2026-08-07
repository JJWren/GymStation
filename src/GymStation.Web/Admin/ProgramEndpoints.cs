using GymStation.Domain.Marketing;
using GymStation.Infrastructure;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Admin;

/// <summary>Row-level admin actions for Programs (#135) — the finance-row idiom:
/// raw forms in, PRG out, ?failed=1 on rejection.</summary>
public static class ProgramEndpoints
{
    public static IEndpointRouteBuilder MapProgramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/actions")
            .RequireAuthorization("Cap:EditLanding")
            .ValidateAntiforgery();

        group.MapPost("/add-program", async ([FromForm] string title, GymStationDbContext db) =>
        {
            var trimmed = (title ?? "").Trim();
            if (trimmed.Length is 0 or > 80)
            {
                return Results.Redirect("/admin/landing/programs?failed=1");
            }

            // GymId rides in via the Added-entity tenant stamp, as everywhere.
            var nextOrder = await db.GymPrograms.MaxAsync(p => (int?)p.SortOrder) ?? 0;
            db.GymPrograms.Add(new GymProgram { Id = Guid.NewGuid(), Title = trimmed, SortOrder = nextOrder + 1 });
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/programs");
        });

        group.MapPost("/update-program", async ([FromForm] Guid programId, [FromForm] string title, [FromForm] string? description, GymStationDbContext db) =>
        {
            var trimmed = (title ?? "").Trim();
            var program = await db.GymPrograms.SingleOrDefaultAsync(p => p.Id == programId);
            if (program is null || trimmed.Length is 0 or > 80)
            {
                return Results.Redirect("/admin/landing/programs?failed=1");
            }

            program.Title = trimmed;
            program.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/programs");
        });

        group.MapPost("/program-archived", async ([FromForm] Guid programId, [FromForm] bool archived, GymStationDbContext db) =>
        {
            var program = await db.GymPrograms.SingleOrDefaultAsync(p => p.Id == programId);
            if (program is not null)
            {
                program.Archived = archived;
                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/landing/programs");
        });

        group.MapPost("/program-move", async ([FromForm] Guid programId, [FromForm] int direction, GymStationDbContext db) =>
        {
            if (direction is not (-1 or 1))
            {
                return Results.Redirect("/admin/landing/programs?failed=1");
            }

            // Actives only: archived rows aren't part of the landing order, and
            // including them here could swap with a row the admin page doesn't
            // show as adjacent (review catch).
            var ordered = await db.GymPrograms.Where(p => !p.Archived).OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync();
            var index = ordered.FindIndex(p => p.Id == programId);
            var target = index + direction;
            if (index >= 0 && target >= 0 && target < ordered.Count)
            {
                // Re-stamp the whole run — SortOrder gaps and ties from history
                // collapse to a clean sequence as a side effect.
                (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
                for (var i = 0; i < ordered.Count; i++)
                {
                    ordered[i].SortOrder = i + 1;
                }

                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/landing/programs");
        });

        return app;
    }
}
