using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.Ops;

public record CreateGymRequest(
    string Name,
    string Slug,
    string TimeZoneId,
    string OwnerEmail,
    string OwnerPassword,
    string OwnerFirstName,
    string OwnerLastName);

/// <summary>
/// Platform-operator surface (v1 has no self-serve gym signup): tenant creation
/// guarded by the Ops:ApiKey configuration value. Disabled entirely when unset.
/// </summary>
public static class OpsEndpoints
{
    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ops/gyms", async (
            CreateGymRequest request,
            HttpContext http,
            IConfiguration config,
            GymStationDbContext db,
            TenantContext tenant,
            UserManager<AppUser> users) =>
        {
            var opsKey = config["Ops:ApiKey"];
            if (string.IsNullOrWhiteSpace(opsKey))
            {
                return Results.NotFound();
            }

            if (http.Request.Headers["X-Ops-Key"] != opsKey)
            {
                return Results.Unauthorized();
            }

            if (TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId, out _) is false)
            {
                return Results.BadRequest(new { error = $"Unknown time zone '{request.TimeZoneId}'." });
            }

            var owner = await users.FindByEmailAsync(request.OwnerEmail);
            if (owner is null)
            {
                owner = new AppUser { Id = Guid.NewGuid(), UserName = request.OwnerEmail, Email = request.OwnerEmail };
                var created = await users.CreateAsync(owner, request.OwnerPassword);
                if (!created.Succeeded)
                {
                    return Results.BadRequest(new { error = string.Join("; ", created.Errors.Select(e => e.Description)) });
                }
            }

            // Ids are client-generated, so gym + settings + owner Person commit in ONE
            // SaveChanges — tenant creation is atomic (no half-created gyms).
            var gym = new Gym
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Slug = request.Slug.ToLowerInvariant(),
                TimeZoneId = request.TimeZoneId,
            };
            db.Gyms.Add(gym);

            // Tenant-owned rows (settings, the owner's Person) are written as the new gym.
            tenant.SetGym(gym.Id);
            db.GymSettings.Add(new GymSettings { GymId = gym.Id });

            // Starter expense taxonomy — fully editable per gym (owner-configurable principle).
            foreach (var name in new[] { "RENT", "INSURANCE", "SOFTWARE", "UTILITIES", "MARKETING" })
            {
                db.ExpenseCategories.Add(new Domain.Money.ExpenseCategory { Id = Guid.NewGuid(), GymId = gym.Id, Name = name });
            }
            db.Persons.Add(new Person
            {
                Id = Guid.NewGuid(),
                GymId = gym.Id,
                UserId = owner.Id,
                FirstName = request.OwnerFirstName,
                LastName = request.OwnerLastName,
                Roles = PersonRoles.Owner | PersonRoles.Admin,
                JoinedOn = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await db.SaveChangesAsync();

            return Results.Created($"/{gym.Slug}", new { gym.Id, gym.Slug });
        });

        // Pitch-demo tenant: full cast, 12 weeks of data. Same ops-key guard; the given
        // password is applied to the key demo logins listed in docs/pitch-walkthrough.md.
        app.MapPost("/ops/seed-demo", async (
            SeedDemoRequest request,
            HttpContext http,
            IConfiguration config,
            GymStation.Infrastructure.Seeding.DemoSeeder seeder,
            UserManager<AppUser> users) =>
        {
            var opsKey = config["Ops:ApiKey"];
            if (string.IsNullOrWhiteSpace(opsKey))
            {
                return Results.NotFound();
            }

            if (http.Request.Headers["X-Ops-Key"] != opsKey)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.DemoPassword) || request.DemoPassword.Length < 10)
            {
                return Results.BadRequest(new { error = "demoPassword is required (min 10 chars) — it activates the walkthrough logins." });
            }

            var slug = (request.Slug ?? "ironworks-bjj").ToLowerInvariant();
            var name = request.Name ?? "Ironworks BJJ";

            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]{3,60}$") || name.Length is < 2 or > 120)
            {
                return Results.BadRequest(new { error = "slug must be 3–60 chars of [a-z0-9-]; name must be 2–120 chars." });
            }

            Guid gymId;
            try
            {
                gymId = await seeder.SeedAsync(slug, name);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            foreach (var handle in new[] { "jordan.torres", "rui.silva", "ana.duarte", "ana.reyes", "sarah.hale" })
            {
                var user = await users.FindByEmailAsync($"{handle}@{slug}.demo");
                if (user is not null)
                {
                    var added = await users.AddPasswordAsync(user, request.DemoPassword);
                    if (!added.Succeeded)
                    {
                        return Results.BadRequest(new { error = $"Password rejected: {string.Join("; ", added.Errors.Select(e => e.Description))}" });
                    }
                }
            }

            return Results.Created($"/{slug}", new { gymId, slug });
        });

        // Round 4.5: the standard TEST tenant — 300 people, every archetype.
        // Unlike the pitch demo's five walkthrough logins, EVERY seeded account
        // gets the password: the whole point is signing in as anyone.
        app.MapPost("/ops/seed-standard", async (
            SeedStandardRequest request,
            HttpContext http,
            IConfiguration config,
            GymStation.Infrastructure.Seeding.StandardSeeder seeder,
            GymStationDbContext db,
            UserManager<AppUser> users) =>
        {
            var opsKey = config["Ops:ApiKey"];
            if (string.IsNullOrWhiteSpace(opsKey))
            {
                return Results.NotFound();
            }

            if (http.Request.Headers["X-Ops-Key"] != opsKey)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.SeedPassword) || request.SeedPassword.Length < 10)
            {
                return Results.BadRequest(new { error = "seedPassword is required (min 10 chars) — it activates every seeded login." });
            }

            var slug = (request.Slug ?? "testworks").ToLowerInvariant();
            var name = request.Name ?? "Testworks Combat Club";

            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]{3,60}$") || name.Length is < 2 or > 120)
            {
                return Results.BadRequest(new { error = "slug must be 3–60 chars of [a-z0-9-]; name must be 2–120 chars." });
            }

            // One transaction across seed + password activation: a failure at any
            // point leaves NO half-seeded tenant behind (review round 1). The
            // UserManager rides the same scoped context, so its writes enlist.
            await using var transaction = await db.Database.BeginTransactionAsync();

            Guid gymId;
            try
            {
                gymId = await seeder.SeedAsync(slug, name);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var suffix = $"@{slug}.demo";
            var seededUsers = await users.Users
                .Where(u => u.Email != null && u.Email.EndsWith(suffix))
                .ToListAsync();
            foreach (var user in seededUsers)
            {
                var added = await users.AddPasswordAsync(user, request.SeedPassword);
                if (!added.Succeeded)
                {
                    return Results.BadRequest(new { error = $"Password rejected for {user.Email}: {string.Join("; ", added.Errors.Select(e => e.Description))}" });
                }
            }

            await transaction.CommitAsync();
            return Results.Created($"/{slug}", new { gymId, slug, logins = seededUsers.Count });
        });

        return app;
    }
}

public record SeedDemoRequest(string? Slug, string? Name, string DemoPassword);

public record SeedStandardRequest(string? Slug, string? Name, string SeedPassword);
