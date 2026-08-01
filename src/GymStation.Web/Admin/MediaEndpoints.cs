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

        // Public pages need logos/heroes without auth. ONLY those two public asset kinds
        // are servable — nothing else that may ever land in the file store (e.g. member
        // portraits) is reachable through this endpoint.
        app.MapGet("/media/{**path}", async (string path, IFileStore store) =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                path, @"^gyms/[0-9a-fA-F-]{36}/(logo|hero)(\.[A-Za-z]+)$");
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
