using GymStation.Infrastructure;
using GymStation.Infrastructure.Storage;
using GymStation.Web.Http;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Admin;

/// <summary>Tenant media: staff upload (logo/hero) + anonymous serving for public pages.</summary>
public static class MediaEndpoints
{
    private const long MaxUploadBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/actions/upload-media", async (HttpRequest request, GymStationDbContext db, IFileStore store) =>
        {
            var form = await request.ReadFormAsync();
            var kind = form["kind"].ToString();
            var file = form.Files.GetFile("file");

            if (kind is not ("logo" or "hero") || file is null || file.Length == 0 || file.Length > MaxUploadBytes)
            {
                return Results.Redirect("/admin/settings?failed=1");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return Results.Redirect("/admin/settings?failed=1");
            }

            var settings = await db.GymSettings.SingleAsync();
            var path = $"gyms/{settings.GymId}/{kind}{extension.ToLowerInvariant()}";

            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(content, path);
            }

            if (kind == "logo")
            {
                settings.LogoPath = path;
            }
            else
            {
                settings.HeroPath = path;
            }

            await db.SaveChangesAsync();
            return Results.Redirect("/admin/settings");
        }).RequireAuthorization("GymStaff").ValidateAntiforgery();

        // Member portraits: staff-only on both sides — stored under the gym's folder,
        // never reachable through the anonymous /media/ route below.
        app.MapPost("/admin/actions/upload-portrait", async (HttpRequest request, GymStationDbContext db, IFileStore store) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (!Guid.TryParse(form["personId"], out var personId))
            {
                return Results.Redirect("/admin/roster");
            }

            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return Results.Redirect($"/admin/people/{personId}?failed=1");
            }

            var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId);
            if (person is null)
            {
                return Results.Redirect("/admin/roster");
            }

            var path = $"gyms/{person.GymId}/portraits/{person.Id}{extension.ToLowerInvariant()}";
            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(content, path);
            }

            person.PortraitPath = path;
            await db.SaveChangesAsync();
            return Results.Redirect($"/admin/people/{personId}");
        }).RequireAuthorization("GymStaff").ValidateAntiforgery();

        // Instructor portraits are PUBLIC (#137, ADR 0003): instructors are the
        // gym's public faces. The route carries the gym because this anonymous
        // request has no slug for the tenant middleware — the query ignores the
        // tenant filter and pins BOTH ids explicitly instead. Everyone else's
        // portrait stays staff-only through the /admin route above.
        app.MapGet("/media/instructor-portrait/{gymId:guid}/{personId:guid}", async (Guid gymId, Guid personId, HttpContext http, GymStationDbContext db, IFileStore store) =>
        {
            var person = await db.Persons.IgnoreQueryFilters()
                .SingleOrDefaultAsync(p => p.Id == personId && p.GymId == gymId);
            if (person is null
                || !GymStation.Domain.People.InstructorPortraits.PubliclyVisible(person)
                || !person.PortraitPath!.StartsWith($"gyms/{person.GymId}/portraits/{person.Id}", StringComparison.OrdinalIgnoreCase)
                || !AllowedTypes.TryGetValue(Path.GetExtension(person.PortraitPath), out var portraitType))
            {
                return Results.NotFound();
            }

            var portraitStream = await store.OpenReadAsync(person.PortraitPath);
            if (portraitStream is null)
            {
                return Results.NotFound();
            }

            // Role loss or archive must take effect immediately — never cache.
            http.Response.Headers.CacheControl = "no-store";
            return Results.Stream(portraitStream, portraitType);
        }).AllowAnonymous();

        // Stories section image (#136): ONE shared image on GymSettings, public
        // via the /media allow-list like logo/hero.
        app.MapPost("/admin/actions/upload-stories-image", async (HttpRequest request, GymStationDbContext db, IFileStore store) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
            {
                return Results.Redirect("/admin/landing/stories?failed=1");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return Results.Redirect("/admin/landing/stories?failed=1");
            }

            var settings = await db.GymSettings.SingleOrDefaultAsync();
            if (settings is null)
            {
                return Results.Redirect("/admin/landing/stories");
            }

            var path = $"gyms/{settings.GymId}/stories{extension.ToLowerInvariant()}";
            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(content, path);
            }

            settings.StoriesImagePath = path;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/stories");
        }).RequireAuthorization("GymStaff").ValidateAntiforgery();

        // Program images (#135): staff upload; PUBLIC serving via the /media
        // allow-list below — programs are marketing content by definition.
        app.MapPost("/admin/actions/upload-program-image", async (HttpRequest request, GymStationDbContext db, IFileStore store) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (!Guid.TryParse(form["programId"], out var programId))
            {
                return Results.Redirect("/admin/landing/programs");
            }

            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
            {
                return Results.Redirect("/admin/landing/programs?failed=1");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return Results.Redirect("/admin/landing/programs?failed=1");
            }

            var program = await db.GymPrograms.SingleOrDefaultAsync(x => x.Id == programId);
            if (program is null)
            {
                return Results.Redirect("/admin/landing/programs");
            }

            var path = $"gyms/{program.GymId}/programs/{program.Id}{extension.ToLowerInvariant()}";
            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(content, path);
            }

            program.ImagePath = path;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/landing/programs");
        }).RequireAuthorization("GymStaff").ValidateAntiforgery();

        // Event flyers (#130): staff upload; every signed-in person of the gym can view
        // (events are member-facing pages), so serving is authed but not staff-gated.
        app.MapPost("/admin/actions/upload-event-image", async (HttpRequest request, GymStationDbContext db, IFileStore store) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (!Guid.TryParse(form["eventId"], out var eventId))
            {
                return Results.Redirect("/admin/events");
            }

            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
            {
                return Results.Redirect("/admin/events?failed=1");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return Results.Redirect("/admin/events?failed=1");
            }

            var gymEvent = await db.GymEvents.SingleOrDefaultAsync(x => x.Id == eventId);
            if (gymEvent is null)
            {
                return Results.Redirect("/admin/events");
            }

            var path = $"gyms/{gymEvent.GymId}/events/{gymEvent.Id}{extension.ToLowerInvariant()}";
            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(content, path);
            }

            gymEvent.ImagePath = path;
            await db.SaveChangesAsync();
            return Results.Redirect("/admin/events");
        }).RequireAuthorization("GymStaff").ValidateAntiforgery();

        app.MapGet("/media/event/{eventId:guid}", async (Guid eventId, HttpContext http, GymStationDbContext db, IFileStore store) =>
        {
            // The tenant query filter scopes the lookup — a cross-gym id is a 404.
            var gymEvent = await db.GymEvents.SingleOrDefaultAsync(x => x.Id == eventId);
            if (gymEvent?.ImagePath is not { } path
                || !path.StartsWith($"gyms/{gymEvent.GymId}/events/{gymEvent.Id}", StringComparison.OrdinalIgnoreCase)
                || !AllowedTypes.TryGetValue(Path.GetExtension(path), out var contentType))
            {
                return Results.NotFound();
            }

            var stream = await store.OpenReadAsync(path);
            if (stream is null)
            {
                return Results.NotFound();
            }

            // Replacements reuse the same URL — don't let browsers keep the old flyer.
            http.Response.Headers.CacheControl = "no-store";
            return Results.Stream(stream, contentType);
        }).RequireAuthorization();

        app.MapGet("/admin/media/portrait/{personId:guid}", async (Guid personId, HttpContext http, GymStationDbContext db, IFileStore store) =>
        {
            var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId);
            if (person?.PortraitPath is not { } path
                // Defense in depth: only ever open THIS person's portrait key, even if
                // the stored value was tampered with — never an arbitrary store path.
                || !path.StartsWith($"gyms/{person.GymId}/portraits/{person.Id}", StringComparison.OrdinalIgnoreCase)
                || !AllowedTypes.TryGetValue(Path.GetExtension(path), out var contentType))
            {
                return Results.NotFound();
            }

            var stream = await store.OpenReadAsync(path);
            if (stream is null)
            {
                return Results.NotFound();
            }

            // Replacements reuse the same URL — don't let browsers keep the old face.
            http.Response.Headers.CacheControl = "no-store";
            return Results.Stream(stream, contentType);
        }).RequireAuthorization("GymStaff");

        // Public pages need logos/heroes/program images without auth. ONLY these
        // public MARKETING asset kinds are servable — nothing else that may ever
        // land in the file store (e.g. member portraits, event flyers) is
        // reachable through this endpoint.
        app.MapGet("/media/{**path}", async (string path, IFileStore store) =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                path, @"^gyms/[0-9a-fA-F-]{36}/(logo|hero|stories|programs/[0-9a-fA-F-]{36})(\.[A-Za-z]+)$");
            if (!match.Success || !AllowedTypes.TryGetValue(match.Groups[2].Value, out var contentType))
            {
                return Results.NotFound();
            }

            var stream = await store.OpenReadAsync(path);
            return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
        }).AllowAnonymous();

        return app;
    }
}
