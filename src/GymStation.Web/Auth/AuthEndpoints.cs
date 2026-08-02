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
                await SignInWithActiveGymAsync(users, signIn, user, gyms[0].GymId);

                // Route by role at sign-in: non-staff never see the admin shell.
                return Results.Redirect(await memberships.LandingPathAsync(user.Id, gyms[0].GymId));
            }

            // Multi-gym: clear the remembered gym so the picker stays mandatory —
            // otherwise the claims factory would resume last session's gym. A failed
            // clear must abort: signing in anyway would silently resume that gym.
            if (user.ActiveGymId is not null)
            {
                user.ActiveGymId = null;
                var cleared = await users.UpdateAsync(user);
                if (!cleared.Succeeded)
                {
                    return Results.Problem("Could not update the account. Try again.", statusCode: 500);
                }
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

            await SignInWithActiveGymAsync(users, signIn, user, gymId);
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

            // Local paths only — never an attacker-suppliable absolute URL. Backslashes
            // and whitespace are rejected too: browsers normalize \ to /, which would
            // turn /\evil.com into a protocol-relative redirect.
            var destination = !string.IsNullOrEmpty(back)
                && back.StartsWith('/')
                && !back.StartsWith("//")
                && !back.Contains('\\')
                && !back.Any(char.IsWhiteSpace)
                ? back
                : "/";
            return Results.Redirect(destination);
        }).RequireAuthorization();

        return app;
    }

    private static async Task SignInWithActiveGymAsync(
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AppUser user, Guid gymId)
    {
        // Persist the choice, then sign in normally: the claims principal factory bakes
        // the active-gym claim into THIS cookie and every later regeneration alike
        // (security-stamp refreshes used to drop a sign-in-only claim — issue #76).
        user.ActiveGymId = gymId;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            // Signing in without the persisted gym would recreate the exact bug this
            // fixes on the next cookie regeneration — fail loudly instead.
            throw new InvalidOperationException(
                $"Failed to persist the active gym: {string.Join("; ", updated.Errors.Select(e => e.Description))}");
        }

        await signIn.SignInAsync(user, isPersistent: true);
    }
}
