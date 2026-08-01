using GymStation.Domain.People;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure;
using GymStation.Infrastructure.Identity;
using GymStation.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;

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

        return app;
    }
}
