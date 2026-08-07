using GymStation.Domain.Marketing;
using GymStation.Infrastructure;
using GymStation.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Admin;

/// <summary>Row-level admin actions for SuccessStories (#136) — the same
/// finance-row idiom as ProgramEndpoints.</summary>
public static class StoryEndpoints
{
    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/actions")
            .RequireAuthorization("Cap:EditLanding")
            .ValidateAntiforgery();

        group.MapPost("/add-story", async ([FromForm] string body, [FromForm] string? attributedTo, GymStationDbContext db) =>
        {
            var trimmed = (body ?? "").Trim();
            var by = (attributedTo ?? "").Trim();
            if (trimmed.Length == 0 || by.Length > 80)
            {
                return Results.Redirect("/admin/landing/stories?failed=1");
            }

            // GymId rides in via the Added-entity tenant stamp, as everywhere.
            var nextOrder = await db.SuccessStories.MaxAsync(s => (int?)s.SortOrder) ?? 0;
            db.SuccessStories.Add(new SuccessStory { Id = Guid.NewGuid(), Body = trimmed, AttributedTo = by.Length == 0 ? null : by, SortOrder = nextOrder + 1 });
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/stories");
        });

        group.MapPost("/update-story", async ([FromForm] Guid storyId, [FromForm] string body, [FromForm] string? attributedTo, GymStationDbContext db) =>
        {
            var trimmed = (body ?? "").Trim();
            var by = (attributedTo ?? "").Trim();
            var story = await db.SuccessStories.SingleOrDefaultAsync(s => s.Id == storyId);
            if (story is null || trimmed.Length == 0 || by.Length > 80)
            {
                return Results.Redirect("/admin/landing/stories?failed=1");
            }

            story.Body = trimmed;
            story.AttributedTo = by.Length == 0 ? null : by;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/stories");
        });

        group.MapPost("/story-archived", async ([FromForm] Guid storyId, [FromForm] bool archived, GymStationDbContext db) =>
        {
            var story = await db.SuccessStories.SingleOrDefaultAsync(s => s.Id == storyId);
            if (story is not null)
            {
                story.Archived = archived;
                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/landing/stories");
        });

        group.MapPost("/story-move", async ([FromForm] Guid storyId, [FromForm] int direction, GymStationDbContext db) =>
        {
            if (direction is not (-1 or 1))
            {
                return Results.Redirect("/admin/landing/stories?failed=1");
            }

            // Actives only — same adjacency contract as program-move.
            var ordered = await db.SuccessStories.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
            var index = ordered.FindIndex(s => s.Id == storyId);
            var target = index + direction;
            if (index >= 0 && target >= 0 && target < ordered.Count)
            {
                (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
                for (var i = 0; i < ordered.Count; i++)
                {
                    ordered[i].SortOrder = i + 1;
                }

                await db.SaveChangesAsync();
            }

            return Results.Redirect("/admin/landing/stories");
        });

        return app;
    }
}
