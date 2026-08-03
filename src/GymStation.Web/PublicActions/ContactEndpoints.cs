using GymStation.Infrastructure;
using GymStation.Infrastructure.Contact;
using GymStation.Infrastructure.Tenancy;
using GymStation.Web.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Web.PublicActions;

/// <summary>The anonymous contact-form POST (#138). Anonymous means NO slug ran
/// through the tenant middleware — the form carries the gym id and this endpoint
/// resumes the tenant explicitly (the instructor-portrait/island precedent),
/// so the service's adds and fan-out stamp the right gym.</summary>
public static class ContactEndpoints
{
    public const string TsPurpose = "GymStation.ContactForm.Timestamp";

    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/public-actions/contact", async (
            [FromForm] Guid gymId,
            [FromForm] string? website,   // honeypot — real visitors never see it
            [FromForm] string? ts,
            [FromForm] string? firstName,
            [FromForm] string? lastName,
            [FromForm] string? email,
            [FromForm] string? phone,
            [FromForm] string? message,
            GymStationDbContext db,
            TenantContext tenant,
            ContactService contact,
            IDataProtectionProvider dataProtection) =>
        {
            var gym = await db.Gyms.IgnoreQueryFilters().SingleOrDefaultAsync(g => g.Id == gymId);
            if (gym is null)
            {
                return Results.NotFound();
            }

            string Back(string query) => $"/{gym.Slug}?{query}#visit";

            // The render stamped a protected timestamp; unprotect proves it's ours
            // and dates the form. Tampered/absent -> reject.
            var age = TimeSpan.MaxValue;
            try
            {
                var raw = dataProtection.CreateProtector(TsPurpose).Unprotect(ts ?? "");
                if (long.TryParse(raw, out var unixMs))
                {
                    age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                }
            }
            catch
            {
                return Results.Redirect(Back("contact=err"));
            }

            tenant.SetGym(gym.Id);
            var outcome = await contact.SubmitAsync(website, age, firstName, lastName, email, phone, message);

            // SilentDrop looks like success on purpose — bots learn nothing.
            return Results.Redirect(outcome == ContactOutcome.Rejected ? Back("contact=err") : Back("sent=1"));
        }).AllowAnonymous().ValidateAntiforgery().RequireRateLimiting("contact");

        return app;
    }
}
