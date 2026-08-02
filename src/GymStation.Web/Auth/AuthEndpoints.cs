using System.Security.Claims;
using GymStation.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using GymStation.Infrastructure.Tenancy;
using GymStation.Web.Tenancy;
using Microsoft.AspNetCore.Identity;

namespace GymStation.Web.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Explicit antiforgery validation for every form post in the group — including
        // /logout, which binds no form fields and would miss the automatic validation
        // that only form-binding endpoints receive.
        var group = app.MapGroup("/auth").AddEndpointFilter(async (ctx, next) =>
        {
            var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(ctx.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

            return await next(ctx);
        });

        group.MapPost("/login", async (
            [FromForm] string email,
            [FromForm] string password,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            GymMembershipService memberships) =>
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                return Results.Redirect("/login?failed=1");
            }

            var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (!check.Succeeded)
            {
                return Results.Redirect("/login?failed=1");
            }

            var gyms = await memberships.GetGymsForUserAsync(user.Id);

            // Single-gym users skip the picker (Q6 caveat: picker only for multi-gym users).
            if (gyms.Count == 1)
            {
                await SignInWithActiveGymAsync(signIn, user, gyms[0].GymId);

                // Route by role at sign-in: non-staff never see the admin shell.
                return Results.Redirect(await memberships.LandingPathAsync(user.Id, gyms[0].GymId));
            }

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Redirect(gyms.Count == 0 ? "/" : "/pick-gym");
        });

        group.MapPost("/pick-gym", async (
            [FromForm] Guid gymId,
            ClaimsPrincipal principal,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            GymMembershipService memberships) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                return Results.Redirect("/login");
            }

            if (!await memberships.IsUserInGymAsync(user.Id, gymId))
            {
                return Results.Redirect("/pick-gym?failed=1");
            }

            await SignInWithActiveGymAsync(signIn, user, gymId);
            return Results.Redirect(await memberships.LandingPathAsync(user.Id, gymId));
        }).RequireAuthorization();

        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Redirect("/login");
        }).RequireAuthorization();

        // Per-user theme preference (decision 3): stored on the account so it follows
        // the user across devices; null means "follow the gym default".
        group.MapPost("/theme", async (
            [FromForm] bool dark,
            [FromForm] string? back,
            ClaimsPrincipal principal,
            UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is not null)
            {
                user.PreferredThemeDark = dark;
                await users.UpdateAsync(user);
            }

            // Local paths only — never an attacker-suppliable absolute URL.
            var destination = !string.IsNullOrEmpty(back) && back.StartsWith('/') && !back.StartsWith("//")
                ? back
                : "/";
            return Results.Redirect(destination);
        }).RequireAuthorization();

        return app;
    }

    private static Task SignInWithActiveGymAsync(SignInManager<AppUser> signIn, AppUser user, Guid gymId)
    {
        // Re-issues the auth cookie with the active gym baked in; ActiveGymMiddleware reads it per request.
        return signIn.SignInWithClaimsAsync(
            user,
            isPersistent: true,
            [new Claim(ActiveGymMiddleware.ActiveGymClaim, gymId.ToString())]);
    }
}
